namespace NServiceBus.TimeoutPersisters.NHibernate
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Transactions;
    using global::NHibernate;
    using NServiceBus.Extensibility;
    using NServiceBus.Persistence;
    using NServiceBus.Persistence.NHibernate;
    using NServiceBus.Transports;
    using Timeout.Core;
    using IsolationLevel = System.Data.IsolationLevel;

    class TimeoutPersister : IPersistTimeouts, IQueryTimeouts
    {
        ISessionFactory SessionFactory;
        ISynchronizedStorageAdapter transportTransactionAdapter;
        ISynchronizedStorage synchronizedStorage;
        string EndpointName;

        public TimeoutPersister(string endpointName,
            ISessionFactory sessionFactory,
            ISynchronizedStorageAdapter transportTransactionAdapter,
            ISynchronizedStorage synchronizedStorage)
        {
            EndpointName = endpointName;
            SessionFactory = sessionFactory;
            this.transportTransactionAdapter = transportTransactionAdapter;
            this.synchronizedStorage = synchronizedStorage;
        }

        public Task<TimeoutsChunk> GetNextChunk(DateTime startSlice)
        {
            using (new TransactionScope(TransactionScopeOption.Suppress))
            {
                var now = DateTime.UtcNow;
                using (var session = SessionFactory.OpenStatelessSession())
                using (var tx = session.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    var results = session.QueryOver<TimeoutEntity>()
                        .Where(x => x.Endpoint == EndpointName)
                        .And(x => x.Time >= startSlice && x.Time <= now)
                        .OrderBy(x => x.Time).Asc
                        .Select(x => x.Id, x => x.Time)
                        .List<object[]>()
                        .Select(p =>
                        {
                            var id = (Guid)p[0];
                            var dueTime = (DateTime)p[1];
                            return new TimeoutsChunk.Timeout(id.ToString(), dueTime);
                        })
                        .ToList();

                    //Retrieve next time we need to run query
                    var startOfNextChunk = session.QueryOver<TimeoutEntity>()
                        .Where(x => x.Endpoint == EndpointName)
                        .Where(x => x.Time > now)
                        .OrderBy(x => x.Time).Asc
                        .Take(1)
                        .SingleOrDefault();

                    var nextTimeToRunQuery = startOfNextChunk?.Time ?? DateTime.UtcNow.AddMinutes(10);

                    tx.Commit();
                    return Task.FromResult(new TimeoutsChunk(results, nextTimeToRunQuery));
                }
            }
        }

        public async Task Add(TimeoutData timeout, ContextBag context)
        {
            var timeoutId = Guid.NewGuid();
            using (var session = await OpenSession(context).ConfigureAwait(false))
            {
                session.Session().Save(new TimeoutEntity
                {
                    Id = timeoutId,
                    Destination = timeout.Destination,
                    SagaId = timeout.SagaId,
                    State = timeout.State,
                    Time = timeout.Time,
                    Headers = ConvertDictionaryToString(timeout.Headers),
                    Endpoint = timeout.OwningTimeoutManager
                });
                await session.CompleteAsync().ConfigureAwait(false);
            }

            timeout.Id = timeoutId.ToString();
        }

        public async Task<bool> TryRemove(string timeoutId, ContextBag context)
        {
            var id = Guid.Parse(timeoutId);
            bool found;
            using (var session = await OpenSession(context).ConfigureAwait(false))
            {
                var queryString = $"delete {typeof(TimeoutEntity)} where Id = :id";
                found = session.Session().CreateQuery(queryString).SetParameter("id", id).ExecuteUpdate() > 0;
                await session.CompleteAsync().ConfigureAwait(false);
            }

            return found;
        }

        async Task<CompletableSynchronizedStorageSession> OpenSession(ContextBag context)
        {
            var transportTransaction = context.GetOrCreate<TransportTransaction>();
            var session = await transportTransactionAdapter.TryAdapt(transportTransaction, context).ConfigureAwait(false)
                ?? await synchronizedStorage.OpenSession(context).ConfigureAwait(false);
            return session;
        }

        public Task RemoveTimeoutBy(Guid sagaId, ContextBag context)
        {
            using (new TransactionScope(TransactionScopeOption.Suppress))
            {
                using (var session = SessionFactory.OpenStatelessSession())
                {
                    using (var tx = session.BeginTransaction(IsolationLevel.ReadCommitted))
                    {
                        var queryString = $"delete {typeof(TimeoutEntity)} where SagaId = :sagaid";
                        session.CreateQuery(queryString)
                            .SetParameter("sagaid", sagaId)
                            .ExecuteUpdate();

                        tx.Commit();

                        return Task.FromResult(0);
                    }
                }

            }
        }

        public async Task<TimeoutData> Peek(string timeoutId, ContextBag context)
        {
            using (var session = await OpenSession(context).ConfigureAwait(false))
            {
                var id = Guid.Parse(timeoutId);
                var te = session.Session().QueryOver<TimeoutEntity>()
                    .Where(x => x.Id == id)
                    .List()
                    .SingleOrDefault();

                var timeout = MapToTimeoutData(te);
                return timeout;
            }
        }


        static TimeoutData MapToTimeoutData(TimeoutEntity te)
        {
            if (te == null)
            {
                return null;
            }

            return new TimeoutData
            {
                Destination = te.Destination,
                Id = te.Id.ToString(),
                SagaId = te.SagaId,
                State = te.State,
                Time = te.Time,
                Headers = ConvertStringToDictionary(te.Headers),
            };
        }

        static Dictionary<string, string> ConvertStringToDictionary(string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return new Dictionary<string, string>();
            }

            return ObjectSerializer.DeSerialize<Dictionary<string, string>>(data);
        }

        static string ConvertDictionaryToString(Dictionary<string, string> data)
        {
            if (data == null || data.Count == 0)
            {
                return null;
            }

            return ObjectSerializer.Serialize(data);
        }

    }
}