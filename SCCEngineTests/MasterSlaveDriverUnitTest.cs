using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Polly;
using Polly.Registry;
using SCCEngine.Domain.DriverLayer;
using SCCEngine.Domain.Models.DriverModels;
using SCCEngine.Domain.Models.TagModels;
using SCCEngine.Driver;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Timers;

namespace SCCEngineTests
{
    [TestFixture]
    public class MasterSlaveDriverTests
    {
        private Mock<ICommunicationChannel> _channelMock;
        private Mock<IProtocol> _protocolMock;
        private Mock<ILogger<MasterSlaveDriver>> _loggerMock;
        private Mock<ResiliencePipelineProvider<string>> _pipelineProviderMock;
        private Channel<Tag> _testChannel;
        private DriverConfiguration _defaultConfig;
        private Tag[] _defaultTags;

        [SetUp]
        public void Setup()
        {
            _channelMock = new Mock<ICommunicationChannel>();
            _protocolMock = new Mock<IProtocol>();
            _loggerMock = new Mock<ILogger<MasterSlaveDriver>>();
            _pipelineProviderMock = new Mock<ResiliencePipelineProvider<string>>();
            _testChannel = Channel.CreateUnbounded<Tag>(); // Используем реальный канал для простоты

            _defaultConfig = new DriverConfiguration
            {
                Id = 1,
                Name = "TestDriver",
                PollingIntervalMs = 500
            };

            _defaultTags = new[] { new Tag() };

            // Настраиваем Polly
            var pipeline = ResiliencePipeline.Empty;
            _pipelineProviderMock
                .Setup(p => p.TryGetPipeline(It.IsAny<string>(), out pipeline))
                .Returns(true);
        }

        private MasterSlaveDriver CreateDriver()
        {
            // Предполагается, что DriverState.ReadyToPolling имеет значение по умолчанию (0), 
            // иначе потребуется инициализация состояния через рефлексию
            return new MasterSlaveDriver(
                _channelMock.Object,
                _protocolMock.Object,
                _testChannel.Writer,
                _defaultTags,
                _loggerMock.Object,
                _defaultConfig,
                _pipelineProviderMock.Object
            );
        }

        [Test]
        public void Constructor_ShouldInitializeProperties()
        {
            // Arrange & Act
            var driver = CreateDriver();

            // Assert
            Assert.That(driver.Channel, Is.EqualTo(_channelMock.Object));
            Assert.That(driver.Protocol, Is.EqualTo(_protocolMock.Object));
            Assert.That(driver.State, Is.Not.EqualTo(DriverState.ReadyToPolling),
                "При создании драйвер не должен сразу быть готов к опросу (пока не запущен)");
        }

        [Test]
        public async Task WriteTagsAsync_WhenCalled_ShouldBuildRequestAndExchangeData()
        {
            // Arrange
            var driver = CreateDriver();
            var tagsToWrite = new[] { new Tag() };

            // Взламываем состояние (устанавливаем ReadyToPolling)
            typeof(MasterSlaveDriver)
                .GetProperty("State")?
                .SetValue(driver, DriverState.ReadyToPolling);

            // Настраиваем IProtocol (тут мы уже исправили тип ранее)
            _protocolMock.Setup(p => p.BuildWriteRequest(
                It.IsAny<IReadOnlyCollection<Tag>>(),
                It.IsAny<Memory<byte>>()))
                .Returns(5);

            // ИСПРАВЛЕНИЕ ЗДЕСЬ: Второй параметр теперь It.IsAny<Memory<byte>>()
            _channelMock.Setup(c => c.ExchangeAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<Memory<byte>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(5);

            // Act
            await driver.WriteTagsAsync(tagsToWrite);

            // Assert
            _protocolMock.Verify(p => p.BuildWriteRequest(
                It.IsAny<IReadOnlyCollection<Tag>>(),
                It.IsAny<Memory<byte>>()), Times.Once);

            // И ИСПРАВЛЕНИЕ ЗДЕСЬ: Второй параметр при проверке тоже It.IsAny<Memory<byte>>()
            _channelMock.Verify(c => c.ExchangeAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<Memory<byte>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task UpdateConfigurationAsync_WhenStateIsReady_ShouldUpdateChannelAndProtocol()
        {
            // Arrange
            var driver = CreateDriver();

            // 1. Устанавливаем правильное состояние
            typeof(MasterSlaveDriver)
                .GetProperty("State")?
                .SetValue(driver, DriverState.ReadyToPolling);

            // 2. ИСПРАВЛЕНИЕ: Теперь, когда состояние верное, добавляем теги. 
            // Без этого _currentTags.Count будет равен 0, и пересборка запроса не запустится!
            driver.UpdateTags(new[] { new Tag() });

            var newChannelMock = new Mock<ICommunicationChannel>();
            var newProtocolMock = new Mock<IProtocol>();
            var newConfig = new DriverConfiguration { Id = 2, Name = "UpdatedDriver", PollingIntervalMs = 1000 };

            // Act
            await driver.UpdateConfigurationAsync(newConfig, newChannelMock.Object, newProtocolMock.Object);

            // Assert
            Assert.That(driver.Channel, Is.EqualTo(newChannelMock.Object));
            Assert.That(driver.Protocol, Is.EqualTo(newProtocolMock.Object));

            // Теперь ожидание вызова сработает, так как теги есть!
            newProtocolMock.Verify(p => p.BuildReadRequest(
                It.IsAny<IReadOnlyCollection<Tag>>(),
                It.IsAny<Memory<byte>>()), Times.Once);
        }

        [Test]
        public void UpdateTags_WhenCalled_ShouldUpdateBuffer()
        {
            // Arrange
            var driver = CreateDriver();
            var newTags = new[] { new Tag(), new Tag() };

            // ВЗЛАМЫВАЕМ СОСТОЯНИЕ ДЛЯ ТЕСТА
            ForceReadyToPollingState(driver);

            _protocolMock.Invocations.Clear();

            // Act
            driver.UpdateTags(newTags);

            // Assert
            // Замените byte[] на Memory<byte> или нужный тип, если нужно
            _protocolMock.Verify(p => p.BuildReadRequest(newTags, It.IsAny<Memory<byte>>()), Times.Once);
        }

        

        [Test]
        public async Task DisposeAsync_ShouldDisposeChannel_AndStopPolling()
        {
            // Arrange
            var driver = CreateDriver();

            // Act
            await driver.DisposeAsync();

            // Assert
            _channelMock.Verify(c => c.DisposeAsync(), Times.Once);
        }

        private void ForceReadyToPollingState(MasterSlaveDriver driver)
        {
            // Находим скрытое поле, которое компилятор генерирует для автосвойства State
            var field = typeof(MasterSlaveDriver).GetField("<State>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            // Принудительно задаем состояние
            field?.SetValue(driver, DriverState.ReadyToPolling);
        }
    }
}
