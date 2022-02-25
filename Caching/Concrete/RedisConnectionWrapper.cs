﻿using Caching.Interfaces;
using Models.Settings;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Net;

namespace Caching.Concrete
{
    public class RedisConnectionWrapper : IRedisConnectionWrapper, ILocker
    {
        #region Fields

        private readonly RedisSettings _config;

        private readonly object _lock = new object();
        private volatile ConnectionMultiplexer _connection;
        private readonly Lazy<string> _connectionString;
        private volatile RedLockFactory _redisLockFactory;

        #endregion

        #region Ctor

        public RedisConnectionWrapper(RedisSettings config)
        {
            _config = config;
            _connectionString = new Lazy<string>(GetConnectionString);
            _redisLockFactory = CreateRedisLockFactory();
        }

        #endregion

        #region Utilities


        protected string GetConnectionString()
        {
            return _config.RedisConnectionString;
        }


        protected ConnectionMultiplexer GetConnection()
        {
            if (_connection != null && _connection.IsConnected) return _connection;

            lock (_lock)
            {
                if (_connection != null && _connection.IsConnected) return _connection;

                _connection?.Dispose();

                _connection = ConnectionMultiplexer.Connect(_connectionString.Value);
            }

            return _connection;
        }


        protected RedLockFactory CreateRedisLockFactory()
        {
            var configurationOptions = ConfigurationOptions.Parse(_connectionString.Value);
            var redLockEndPoints = GetEndPoints().Select(endPoint => new RedLockEndPoint
            {
                EndPoint = endPoint,
                Password = configurationOptions.Password,
                Ssl = configurationOptions.Ssl,
                RedisDatabase = configurationOptions.DefaultDatabase,
                ConfigCheckSeconds = configurationOptions.ConfigCheckSeconds,
                ConnectionTimeout = configurationOptions.ConnectTimeout,
                SyncTimeout = configurationOptions.SyncTimeout
            }).ToList();

            return RedLockFactory.Create(redLockEndPoints);
        }

        #endregion

        #region Methods

        public IDatabase GetDatabase(int db)
        {
            return GetConnection().GetDatabase(db);
        }

        public IServer GetServer(EndPoint endPoint)
        {
            return GetConnection().GetServer(endPoint);
        }
        public EndPoint[] GetEndPoints()
        {
            return GetConnection().GetEndPoints();
        }

        public void FlushDatabase(RedisDatabaseNumber db)
        {
            var endPoints = GetEndPoints();

            foreach (var endPoint in endPoints)
            {
                GetServer(endPoint).FlushDatabase((int)db);
            }
        }

        public bool PerformActionWithLock(string resource, TimeSpan expirationTime, Action action)
        {
            using (var redisLock = _redisLockFactory.CreateLock(resource, expirationTime))
            {
                if (!redisLock.IsAcquired)
                    return false;

                action();

                return true;
            }
        }

        public void Dispose()
        {
            _connection?.Dispose();

            _redisLockFactory?.Dispose();
        }



        #endregion
    }
}