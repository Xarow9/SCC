using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SCCEngine.Domain.Models.TagModels;

namespace SCCEngine.Domain.TagLayer
{
    public class TagManager
    {
        private Tag[] tags;
        private int tagCount;
        ChannelReader<Tag> channel;
        ILogger<TagManager> logger;


        public TagManager(Tag[] tags, ChannelReader<Tag> channel, ILogger<TagManager> logger)
        {
            this.tags = tags;
            this.channel = channel;
            this.logger = logger;
            
            tagCount = tags.Length;
        }

        public async Task ConsumeTagsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Передаем токен для корректного завершения
                await foreach (Tag tag in channel.ReadAllAsync(cancellationToken))
                {
                    // 1. Однократный доступ к элементу коллекции.
                    if ((uint)tag.Id >= (uint)tagCount) 
                    {
                        // Логируем или инкрементируем счетчик "битых" пакетов
                        continue;
                    }
                    ref var target = ref tags[tag.Id];

                    switch (tag.MsgType)
                    {
                        case MsgType.Activate:
                            if (!target.IsActive)
                            {
                                /* Счетчик битых пакетов +1 */
                            }
                            target.IsActive = true;
                            break;

                        case MsgType.Deactivate:
                            if (target.IsActive)
                            {
                                /* Счетчик битых пакетов +1 */
                            }
                            target.IsActive = false;
                            break;

                        case MsgType.Value:
                            if (!target.IsActive)
                            {
                                /* Счетчик битых пакетов +1 */
                            }

                            target.Quality = tag.Quality;
                            target.Timestamp = tag.Timestamp;

                            // Избегаем лишнего break, делаем код более линейным
                            if (tag.Quality == Quality.Good)
                            {
                                target.Value = tag.Value;                                
                            }
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Нормальное поведение при отмене через CancellationToken.
                // Просто выходим из метода.
            }
            catch (Exception ex)
            {
                // ОБЯЗАТЕЛЬНО залогировать ошибку, иначе вы не узнаете, почему консьюмер умер
                logger.LogError(ex.Message, "Критическая ошибка при чтении тегов");
                throw;
            }
        }
    }
}
