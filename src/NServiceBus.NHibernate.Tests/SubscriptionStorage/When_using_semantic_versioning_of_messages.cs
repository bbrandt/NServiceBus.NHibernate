namespace NServiceBus.Unicast.Subscriptions.NHibernate.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using NServiceBus.Extensibility;
    using NUnit.Framework;

    [TestFixture]
    class When_using_semantic_versioning_of_messages : InMemoryDBFixture
    {
        [Test]
        public async Task Only_changes_in_major_version_should_effect_subscribers()
        {
            await storage.Subscribe(TestClients.ClientA, MessageTypes.MessageA, new ContextBag());
            await storage.Subscribe(TestClients.ClientB, MessageTypes.MessageAv11, new ContextBag());
            await storage.Subscribe(TestClients.ClientC, MessageTypes.MessageAv2, new ContextBag());

            Assert.AreEqual(2, (await storage.GetSubscriberAddressesForMessage(new List<MessageType> { MessageTypes.MessageA }, new ContextBag())).Count()); 
            Assert.AreEqual(2, (await storage.GetSubscriberAddressesForMessage(new List<MessageType> { MessageTypes.MessageAv11 }, new ContextBag())).Count());
            Assert.AreEqual(1, (await storage.GetSubscriberAddressesForMessage(new List<MessageType> { MessageTypes.MessageAv2 }, new ContextBag())).Count());
        }
    }
}