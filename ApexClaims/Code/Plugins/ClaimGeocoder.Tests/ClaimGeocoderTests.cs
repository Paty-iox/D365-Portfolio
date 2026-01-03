using System;
using Microsoft.Xrm.Sdk;
using Moq;
using Xunit;

namespace ApexClaims.Plugins.Tests
{
    public class ClaimGeocoderTests
    {
        private const string ClaimEntityName = "new_claim";
        private const string IncidentLocationField = "new_incidentlocation";

        private Mock<IPluginExecutionContext> _contextMock;
        private Mock<IOrganizationServiceFactory> _serviceFactoryMock;
        private Mock<IOrganizationService> _serviceMock;
        private Mock<ITracingService> _traceMock;
        private Mock<IServiceProvider> _serviceProviderMock;

        public ClaimGeocoderTests()
        {
            _contextMock = new Mock<IPluginExecutionContext>();
            _serviceFactoryMock = new Mock<IOrganizationServiceFactory>();
            _serviceMock = new Mock<IOrganizationService>();
            _traceMock = new Mock<ITracingService>();
            _serviceProviderMock = new Mock<IServiceProvider>();

            _serviceFactoryMock
                .Setup(f => f.CreateOrganizationService(It.IsAny<Guid?>()))
                .Returns(_serviceMock.Object);

            _serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IPluginExecutionContext)))
                .Returns(_contextMock.Object);
            _serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IOrganizationServiceFactory)))
                .Returns(_serviceFactoryMock.Object);
            _serviceProviderMock
                .Setup(sp => sp.GetService(typeof(ITracingService)))
                .Returns(_traceMock.Object);
        }

        [Fact]
        public void Execute_WrongEntity_SkipsProcessing()
        {
            // Arrange
            _contextMock.Setup(c => c.PrimaryEntityName).Returns("contact");
            _contextMock.Setup(c => c.MessageName).Returns("Create");
            _contextMock.Setup(c => c.InputParameters).Returns(new ParameterCollection());

            var plugin = new ClaimGeocoder();

            // Act
            plugin.Execute(_serviceProviderMock.Object);

            // Assert - no updates should be made
            _serviceMock.Verify(s => s.Update(It.IsAny<Entity>()), Times.Never);
            _traceMock.Verify(t => t.Trace(It.Is<string>(msg => msg.Contains("Wrong entity")), It.IsAny<object[]>()), Times.Once);
        }

        [Fact]
        public void Execute_WrongMessage_SkipsProcessing()
        {
            // Arrange
            _contextMock.Setup(c => c.PrimaryEntityName).Returns(ClaimEntityName);
            _contextMock.Setup(c => c.MessageName).Returns("Delete");
            _contextMock.Setup(c => c.InputParameters).Returns(new ParameterCollection());

            var plugin = new ClaimGeocoder();

            // Act
            plugin.Execute(_serviceProviderMock.Object);

            // Assert
            _serviceMock.Verify(s => s.Update(It.IsAny<Entity>()), Times.Never);
            _traceMock.Verify(t => t.Trace(It.Is<string>(msg => msg.Contains("Wrong message")), It.IsAny<object[]>()), Times.Once);
        }

        [Fact]
        public void Execute_NoTargetEntity_SkipsProcessing()
        {
            // Arrange
            _contextMock.Setup(c => c.PrimaryEntityName).Returns(ClaimEntityName);
            _contextMock.Setup(c => c.MessageName).Returns("Create");
            _contextMock.Setup(c => c.InputParameters).Returns(new ParameterCollection());

            var plugin = new ClaimGeocoder();

            // Act
            plugin.Execute(_serviceProviderMock.Object);

            // Assert
            _serviceMock.Verify(s => s.Update(It.IsAny<Entity>()), Times.Never);
            _traceMock.Verify(t => t.Trace(It.Is<string>(msg => msg.Contains("No target")), It.IsAny<object[]>()), Times.Once);
        }

        [Fact]
        public void Execute_UpdateWithoutLocationChange_SkipsProcessing()
        {
            // Arrange
            var target = new Entity(ClaimEntityName, Guid.NewGuid());
            target["new_someotherfield"] = "value";

            var inputParams = new ParameterCollection { { "Target", target } };

            _contextMock.Setup(c => c.PrimaryEntityName).Returns(ClaimEntityName);
            _contextMock.Setup(c => c.MessageName).Returns("Update");
            _contextMock.Setup(c => c.InputParameters).Returns(inputParams);
            _contextMock.Setup(c => c.UserId).Returns(Guid.NewGuid());

            var plugin = new ClaimGeocoder();

            // Act
            plugin.Execute(_serviceProviderMock.Object);

            // Assert - no coordinate updates since location wasn't changed
            _serviceMock.Verify(s => s.Update(It.IsAny<Entity>()), Times.Never);
            _traceMock.Verify(t => t.Trace(It.Is<string>(msg => msg.Contains("not changed")), It.IsAny<object[]>()), Times.Once);
        }

        [Fact]
        public void Execute_EmptyLocation_ClearsCoordinates()
        {
            // Arrange
            var claimId = Guid.NewGuid();
            var target = new Entity(ClaimEntityName, claimId);
            target[IncidentLocationField] = "";

            var inputParams = new ParameterCollection { { "Target", target } };

            _contextMock.Setup(c => c.PrimaryEntityName).Returns(ClaimEntityName);
            _contextMock.Setup(c => c.MessageName).Returns("Create");
            _contextMock.Setup(c => c.InputParameters).Returns(inputParams);
            _contextMock.Setup(c => c.UserId).Returns(Guid.NewGuid());

            // Mock empty environment variables result
            _serviceMock
                .Setup(s => s.RetrieveMultiple(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryBase>()))
                .Returns(new EntityCollection());

            var plugin = new ClaimGeocoder();

            // Act
            plugin.Execute(_serviceProviderMock.Object);

            // Assert - coordinates should be cleared (set to null)
            _serviceMock.Verify(s => s.Update(It.Is<Entity>(e =>
                e.LogicalName == ClaimEntityName &&
                e.Id == claimId &&
                e.Contains("new_incidentlatitude") &&
                e.Contains("new_incidentlongitude") &&
                e["new_incidentlatitude"] == null &&
                e["new_incidentlongitude"] == null
            )), Times.Once);
        }

        [Fact]
        public void Execute_MissingEnvironmentVariables_SkipsGeocoding()
        {
            // Arrange
            var claimId = Guid.NewGuid();
            var target = new Entity(ClaimEntityName, claimId);
            target[IncidentLocationField] = "123 Test Street, Sydney NSW";

            var inputParams = new ParameterCollection { { "Target", target } };

            _contextMock.Setup(c => c.PrimaryEntityName).Returns(ClaimEntityName);
            _contextMock.Setup(c => c.MessageName).Returns("Create");
            _contextMock.Setup(c => c.InputParameters).Returns(inputParams);
            _contextMock.Setup(c => c.UserId).Returns(Guid.NewGuid());

            // Mock empty environment variables - not configured
            _serviceMock
                .Setup(s => s.RetrieveMultiple(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryBase>()))
                .Returns(new EntityCollection());

            var plugin = new ClaimGeocoder();

            // Act
            plugin.Execute(_serviceProviderMock.Object);

            // Assert - should skip with message about missing config
            _traceMock.Verify(t => t.Trace(It.Is<string>(msg => msg.Contains("must be configured")), It.IsAny<object[]>()), Times.Once);
            _serviceMock.Verify(s => s.Update(It.IsAny<Entity>()), Times.Never);
        }

        [Theory]
        [InlineData("Create")]
        [InlineData("create")]
        [InlineData("CREATE")]
        public void Execute_CreateMessageCaseInsensitive_AcceptsMessage(string messageName)
        {
            // Arrange
            var target = new Entity(ClaimEntityName, Guid.NewGuid());
            target[IncidentLocationField] = "Test Location";

            var inputParams = new ParameterCollection { { "Target", target } };

            _contextMock.Setup(c => c.PrimaryEntityName).Returns(ClaimEntityName);
            _contextMock.Setup(c => c.MessageName).Returns(messageName);
            _contextMock.Setup(c => c.InputParameters).Returns(inputParams);
            _contextMock.Setup(c => c.UserId).Returns(Guid.NewGuid());

            _serviceMock
                .Setup(s => s.RetrieveMultiple(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryBase>()))
                .Returns(new EntityCollection());

            var plugin = new ClaimGeocoder();

            // Act
            plugin.Execute(_serviceProviderMock.Object);

            // Assert - should not reject due to wrong message
            _traceMock.Verify(t => t.Trace(It.Is<string>(msg => msg.Contains("Wrong message")), It.IsAny<object[]>()), Times.Never);
        }
    }
}
