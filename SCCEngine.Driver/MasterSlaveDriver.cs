using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;
using Polly.Retry;
using SCCEngine.Domain.DriverLayer;
using SCCEngine.Domain.Models.DriverModels;
using SCCEngine.Domain.Models.TagModels;
using System.Buffers;
using System.Threading.Channels;

namespace SCCEngine.Driver
{
    public class MasterSlaveDriver : IDriver
    {
        private int _id;
        private string _name;
        private int _pollingIntervalMs;
        private readonly ILogger<MasterSlaveDriver> _logger;
        private readonly ResiliencePipeline _pipeline;

        private ChannelWriter<Tag> _channelWriter;
        private CancellationTokenSource? _pollingCts;
        
        // TODO [Жизненный цикл]: Раскомментировать поле ниже. Оно необходимо для корректного ожидания завершения фоновой задачи в StopPollingAsync.
        // private Task? _pollingTask;


        // 1. Предвыделенный буфер. Выделяем память один раз при создании драйвера.
        private readonly byte[] _requestBuffer = new byte[1024];
        private ReadOnlyMemory<byte> _requestReadyToSend;
        //private int _requestBufferLength;

        // 2. Хранилище актуальных тегов для опроса и объект для синхронизации
        private IReadOnlyCollection<Tag> _currentTags = Array.Empty<Tag>();
        private readonly object _syncLock = new object();

        // 3. Буффер ответа
        private readonly byte[] _responseBuffer = new byte[1024];

        public MasterSlaveDriver(
            ICommunicationChannel channel,
            IProtocol protocol,
            ChannelWriter<Tag> channelWriter,
            Tag[] currentTags,
            ILogger<MasterSlaveDriver> logger,
            DriverConfiguration config,
            ResiliencePipelineProvider<string> pipelineProvider
            )
        {
            Channel = channel;
            Protocol = protocol;
            _channelWriter = channelWriter;
            _logger = logger;
            _id = config.Id;
            _name = config.Name;

            _pollingIntervalMs = config.PollingIntervalMs.GetValueOrDefault(1000);
            if (_pollingIntervalMs <= 0) _pollingIntervalMs = 1000;

            // TODO : Адекватно настроить стратегию повторов Polly
            if (!pipelineProvider.TryGetPipeline(_name, out _pipeline))
            {
                _pipeline = pipelineProvider.GetPipeline("Default");
                _logger.LogInformation("Для драйвера {Name} не найдены специфичные настройки Polly. Используется Default.", _name);
            }
            else
            {
                _logger.LogInformation("Политики Polly для драйвера {Name} успешно загружены из конфигурации.", _name);
            }

            UpdateTags(currentTags);
        }
        public ICommunicationChannel Channel { get; private set; }
        public IProtocol Protocol { get; private set; }
        public DriverState State { get; private set; }

        // TODO [Жизненный цикл]: StartPollingAsync не должен блокировать вызывающий поток (сейчас while крутится прямо в нём).
        // Необходимо:
        // 1. Вынести блок try/catch с while в отдельный приватный метод (например, PollingLoopAsync(CancellationToken ct)).
        // 2. В StartPollingAsync оставить только инициализацию и запуск: _pollingTask = Task.Run(() => PollingLoopAsync(_pollingCts.Token));
        public async Task StartPollingAsync()
        {
            _logger.LogInformation($"Запуск цикла опроса драйвера: {_name}| ID: {_id}");
            _pollingCts = new CancellationTokenSource();

            // TODO [Рефакторинг]: Удалить комментарий ниже. Настройка Serilog не относится к этому классу, она делается при старте приложения (Program.cs).
            // TODO: Настроить нормальный логгер (Serilog)
            try
            {
                State = DriverState.Polling;

                while (!_pollingCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        if (!Channel.IsConnected)
                        {
                            await Task.Delay(1000, _pollingCts.Token);
                            continue;
                        }

                        ReadOnlyMemory<byte> requestPayload;
                        IReadOnlyCollection<Tag> tagsToUpdate;

                        // Берем безопасный слепок текущего состояния
                        lock (_syncLock)
                        {
                            requestPayload = _requestReadyToSend;
                            tagsToUpdate = _currentTags;
                        }

                        if (requestPayload.IsEmpty)
                        {
                            await Task.Delay(500, _pollingCts.Token);
                            continue;
                        }

                        // 4. Ожидаем ответ от устройства
                        int bytesRead = await _pipeline.ExecuteAsync(
                            async ct => await Channel.ExchangeAsync(
                                requestPayload, _responseBuffer, _pollingCts.Token
                                ),
                            _pollingCts.Token
                            );

                        if (bytesRead > 0)
                        {
                            // 5. Ограничиваем буфер только полученными данными
                            ReadOnlyMemory<byte> responsePayload = _responseBuffer.AsMemory(0, bytesRead);

                            // 6. Передаем ответ в протокол для разбора и обновления значений тегов
                            Protocol.ParseReadResponse(responsePayload, tagsToUpdate);

                            // 7. Публикуем обновленные теги в канал для потребителя
                            foreach (var tag in tagsToUpdate)
                            {
                                // Используем WaitToWriteAsync для обработки Backpressure
                                if (await _channelWriter.WaitToWriteAsync(_pollingCts.Token))
                                {
                                    await _channelWriter.WriteAsync(tag, _pollingCts.Token);
                                }
                            }
                        }

                        // 8. Пауза между циклами опроса оборудования
                        await Task.Delay(_pollingIntervalMs, _pollingCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // Пробрасываем во внешний catch для штатной остановки
                    }
                    catch (Exception ex)
                    {
                        // Ошибка одного цикла. Логируем и пробуем снова!
                        _logger.LogWarning(ex, "Сбой при опросе. Повторная попытка через {Delay} мс", _pollingIntervalMs);
                        await Task.Delay(_pollingIntervalMs, _pollingCts.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Это не ошибка, а штатная остановка драйвера
                // TODO [Опечатка]: Правильнее назвать состояние ReadyToPolling (Poll - опрос), а не Pool (бассейн). Изменить в самом Enum DriverState.
                State = DriverState.ReadyToPolling;
            }
            catch (Exception ex)
            {
                // TODO [Логирование]: Заменить строку ниже на реальный вызов логирования:
                // _logger.LogCritical(ex, "Критическая ошибка драйвера '{Name}' (ID: {Id}). Цикл опроса остановлен.", _name, _id);
                // TODO: Логирование непредвиденной ошибки (обрыв связи, сбой порта и т.д.)
                State = DriverState.Faulted;
            }
            // TODO [Состояние]: Добавить блок finally, в котором делать проверку `if (State == DriverState.Polling) State = DriverState.ReadyToPooling;`,
            // чтобы гарантированно сбрасывать статус в случае непредвиденного выхода из цикла.
        }
        public async Task StopPollingAsync()
        {
            if (_pollingCts != null)
            {
                _pollingCts.Cancel();

                // Дожидаемся реального завершения фонового цикла
                // TODO [Жизненный цикл]: Раскомментировать _pollingTask в начале файла, чтобы этот код компилировался.
                /*
                if (_pollingTask != null)
                {
                    await _pollingTask;
                }
                */

                _pollingCts.Dispose();
                _pollingCts = null;

                // TODO [Жизненный цикл]: Раскомментировать очистку задачи после ее завершения
                //_pollingTask = null;
            }
        }

        public async Task UpdateConfigurationAsync(
            DriverConfiguration? config,
            ICommunicationChannel? channel,
            IProtocol? protocol,
            CancellationToken cancellationToken = default
            )
        {
            // 1. Проверка состояния с логированием
            if (State != DriverState.ReadyToPolling)
            {
                _logger.LogWarning("Попытка обновить конфигурацию отклонена. Драйвер не находится в состоянии ReadyToPooling (Текущее состояние: {State}).", State);
                return;
            }

            bool needsRebuild = false;

            // 2. Обновление канала
            if (channel != null)
            {
                Channel = channel;
            }

            // 3. Обновление протокола
            if (protocol != null)
            {
                Protocol = protocol;
                needsRebuild = true; // Сигнализируем, что правила сборки байтов изменились
            }

            // 4. Применение конфигурации
            if (config != null)
            {
                _id = config.Id;
                _name = config.Name;
                _pollingIntervalMs = config.PollingIntervalMs.GetValueOrDefault(1000);
                if (_pollingIntervalMs <= 0) _pollingIntervalMs = 1000;
            }

            // 5. Пересборка запроса, если протокол изменился и теги уже заданы
            if (needsRebuild && _currentTags.Count > 0)
            {
                UpdateTags(_currentTags);
                _logger.LogDebug("Запрос пересобран из-за смены протокола.");
            }

            await Task.CompletedTask;
        }

        // TODO [Логика тегов]: Реализовать логику пробросов сигналов об активации и деактивации тегов.
        // 1. Сравнивать старый список `_currentTags` с новым `tags` (находить добавленные и удаленные теги).
        // 2. Исключать деактивированные теги из метода BuildReadRequest (или передавать в Protocol только активные).
        // 3. Отправлять системное событие (или сам удаленный тег со статусом Quality = Bad) в _channelWriter, чтобы остальная система поняла, что тег больше не опрашивается.
        public void UpdateTags(IReadOnlyCollection<Tag> tags)
        {
            if (State != DriverState.ReadyToPolling)
            {
                //_logger.LogWarning("Попытка обновить конфигурацию отклонена. Драйвер не находится в состоянии ReadyToPooling (Текущее состояние: {State}).", State);
                return;
            }
            lock (_syncLock)
            {
                _currentTags = tags;
                int length = Protocol.BuildReadRequest(_currentTags, _requestBuffer);
                _requestReadyToSend = _requestBuffer.AsMemory(0, length);
            }
        }

        public async Task WriteTagsAsync(IReadOnlyCollection<Tag> tags, CancellationToken cancellationToken = default)
        {
            // 1. Проверка состояния
            if (State != DriverState.ReadyToPolling && State != DriverState.Polling)
            {
                _logger.LogWarning("Попытка записи тегов отклонена. Текущее состояние: {State}", State);
                return;
            }

            byte[] writeBuffer = ArrayPool<byte>.Shared.Rent(1024);
            byte[] responseBuffer = ArrayPool<byte>.Shared.Rent(1024);

            try
            {
                int length = Protocol.BuildWriteRequest(tags, writeBuffer);
                ReadOnlyMemory<byte> payload = writeBuffer.AsMemory(0, length);

                int bytesRead = await Channel.ExchangeAsync(payload, responseBuffer, cancellationToken);
                if (bytesRead > 0)
                {
                    // Опционально: проверить ответ устройства, успешна ли запись
                    // Protocol.ParseWriteResponse(responseBuffer.AsMemory(0, bytesRead));
                    _logger.LogInformation("Успешная запись {Count} тегов в устройство.", tags.Count);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(writeBuffer);
                ArrayPool<byte>.Shared.Return(responseBuffer);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopPollingAsync();
            await Channel.DisposeAsync();

            // TODO [Освобождение ресурсов]: Если драйвер является эксклюзивным владельцем _channelWriter, 
            // следует закрывать его здесь, вызвав _channelWriter.Complete();
        }
    }
}