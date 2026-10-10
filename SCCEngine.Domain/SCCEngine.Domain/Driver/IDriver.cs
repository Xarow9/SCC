using System;
using System.Collections.Generic;
using System.Text;
using SCCEngine.Domain.Models.DriverModels;
using SCCEngine.Domain.Models.TagModels;

namespace SCCEngine.Domain.DriverLayer
{
    public interface IDriver : IAsyncDisposable
    {
        IProtocol Protocol { get; }
        ICommunicationChannel Channel { get; }

        // Текущий статус драйвера (полезно для UI и watchdog-сервисов)
        DriverState State { get; }

        // Управление жизненным циклом (запуск и остановка цикла опроса)
        Task StartPollingAsync();
        Task StopPollingAsync();

        // Управление конфигурацией и списком тегов 
        Task UpdateConfigurationAsync(
            DriverConfiguration? config,
            ICommunicationChannel? channel,
            IProtocol? protocol,
            CancellationToken cancellationToken = default
            );
        void UpdateTags(IReadOnlyCollection<Tag> tags);


        Task WriteTagsAsync(IReadOnlyCollection<Tag> tags, CancellationToken cancellationToken = default);
    }

    // Перечисление состояний для мониторинга
    public enum DriverState
    {
        Disconnected,
        Connecting,
        Polling,
        ReadyToPolling,
        Faulted
    }
}
