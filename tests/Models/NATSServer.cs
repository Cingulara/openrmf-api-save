using Xunit;
using openrmf_save_api.Models;
using System;
using NATS.Client;

namespace tests.Models
{
    public class NATSServerTests
    {
        [Fact]
        public void Test_NewNATSServerIsValid()
        {
            NATSServer nats = new NATSServer();
            Assert.True(nats != null);
            Assert.True(nats.connection == null);
        }
    }
}
