﻿namespace NServiceBus.Persistence.NHibernate
{
    using System;
    using System.Data.SqlClient;
    using System.Threading.Tasks;
    using global::NHibernate;
    using global::NHibernate.Impl;
    using NServiceBus.Extensibility;
    using NServiceBus.Outbox;
    using NServiceBus.Outbox.NHibernate;
    using NServiceBus.Persistence;
    using NServiceBus.Transports;

    class NHibernateSynchronizedStorageAdapter : ISynchronizedStorageAdapter
    {
        ISessionFactory sessionFactory;
        static readonly Task<CompletableSynchronizedStorageSession> EmptyResult = Task.FromResult((CompletableSynchronizedStorageSession) null);

        public NHibernateSynchronizedStorageAdapter(ISessionFactory sessionFactory)
        {
            this.sessionFactory = sessionFactory;
        }
        
        public Task<CompletableSynchronizedStorageSession> TryAdapt(OutboxTransaction transaction, ContextBag context)
        {
            var nhibernateTransaction = transaction as NHibernateOutboxTransaction;
            if (nhibernateTransaction != null)
            {
                CompletableSynchronizedStorageSession session = new NHibernateNativeTransactionSynchronizedStorageSession(nhibernateTransaction.Session, nhibernateTransaction.Transaction, false);
                return Task.FromResult(session);
            }
            return EmptyResult;
        }

        public Task<CompletableSynchronizedStorageSession> TryAdapt(TransportTransaction transportTransaction, ContextBag context)
        {
            System.Transactions.Transaction ambientTransaction;
            if (transportTransaction.TryGet(out ambientTransaction))
            {
                SqlConnection existingSqlConnection;
                if (transportTransaction.TryGet(out existingSqlConnection)) //SQL server transport in ambient TX mode
                {
                    CompletableSynchronizedStorageSession session = new NHibernateAmbientTransactionSynchronizedStorageSession(sessionFactory.OpenSession(existingSqlConnection), existingSqlConnection, false);
                    return Task.FromResult(session);
                }
                else //Other transport in ambient TX mode
                {
                    var sessionFactoryImpl = sessionFactory as SessionFactoryImpl;
                    if (sessionFactoryImpl == null)
                    {
                        throw new NotSupportedException("Overriding default implementation of ISessionFactory is not supported.");
                    }
                    var connection = sessionFactoryImpl.ConnectionProvider.GetConnection();
                    CompletableSynchronizedStorageSession session = new NHibernateAmbientTransactionSynchronizedStorageSession(sessionFactory.OpenSession(connection), connection, true);
                    return Task.FromResult(session);
                }
            }
            return EmptyResult;
        }
    }
}