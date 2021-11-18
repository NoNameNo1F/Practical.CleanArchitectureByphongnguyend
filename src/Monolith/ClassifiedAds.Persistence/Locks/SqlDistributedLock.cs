﻿using ClassifiedAds.CrossCuttingConcerns.Locks;
using Microsoft.Data.SqlClient;
using System;
using System.Data;

namespace ClassifiedAds.Persistence.Locks
{
    public class SqlDistributedLock : IDistributedLock
    {
        public const int AlreadyHeldReturnCode = 103;

        private readonly SqlConnection _connection;

        public SqlDistributedLock(SqlConnection connection)
        {
            _connection = connection;
        }

        public IDistributedLockScope Acquire(string lockName)
        {
            SqlParameter returnValue;
            var acquireCommand = CreateAcquireCommand(_connection, 0, lockName, -1, out returnValue);

            acquireCommand.ExecuteNonQuery();

            if (ParseReturnCode((int)returnValue.Value))
            {
                return new SqlDistributedLockScope(_connection, lockName);
            }
            else
            {
                return null;
            }
        }

        public IDistributedLockScope TryAcquire(string lockName)
        {
            SqlParameter returnValue;
            var acquireCommand = CreateAcquireCommand(_connection, 30, lockName, 0, out returnValue);

            acquireCommand.ExecuteNonQuery();

            if (ParseReturnCode((int)returnValue.Value))
            {
                return new SqlDistributedLockScope(_connection, lockName);
            }
            else
            {
                return null;
            }
        }

        private static SqlCommand CreateAcquireCommand(SqlConnection connection, int commandTimeout, string lockName, int lockTimeout, out SqlParameter returnValue)
        {
            SqlCommand command = connection.CreateCommand();
            returnValue = command.Parameters.Add(new SqlParameter { ParameterName = "Result", DbType = DbType.Int32, Direction = ParameterDirection.Output });
            command.CommandText =
                $@"IF APPLOCK_MODE('public', @Resource, @LockOwner) != 'NoLock'
                            SET @Result = {AlreadyHeldReturnCode}
                        ELSE
                            EXEC @Result = dbo.sp_getapplock @Resource=@Resource, @LockMode=@LockMode, @LockOwner=@LockOwner, @LockTimeout=@LockTimeout, @DbPrincipal='public'"
            ;

            command.CommandTimeout = commandTimeout;

            command.Parameters.Add(new SqlParameter("Resource", lockName));
            command.Parameters.Add(new SqlParameter("LockMode", "Exclusive"));
            command.Parameters.Add(new SqlParameter("LockOwner", "Session"));
            command.Parameters.Add(new SqlParameter("LockTimeout", lockTimeout));

            return command;
        }

        /// <summary>
        /// sp_getapplock exit codes documented at
        /// https://docs.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql#return-code-values
        /// </summary>
        /// <param name="returnCode">code returned after calling sp_getapplock</param>
        /// <returns>true/false</returns>
        public static bool ParseReturnCode(int returnCode)
        {
            switch (returnCode)
            {
                case 0:
                case 1:
                    return true;
                case -1:
                    return false;
                case -2:
                    throw new OperationCanceledException("The lock request was canceled.");
                case -3:
                    throw new Exception("The lock request was chosen as a deadlock victim.");
                case -999:
                    throw new ArgumentException("parameter validation or other error");
                case AlreadyHeldReturnCode:
                    return false;
            }

            if (returnCode <= 0)
            {
                throw new InvalidOperationException($"Could not acquire lock with return code: {returnCode}");
            }

            return false;
        }
    }
}
