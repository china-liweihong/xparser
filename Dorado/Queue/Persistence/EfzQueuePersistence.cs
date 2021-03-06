using System.Collections.Generic;
using System.Data.EffiProz;
using System.Text;

namespace Dorado.Queue.Persistence
{
    public class EfzQueuePersistence<T> : QueuePersistence<T> where T : class, new()
    {
        private string connString;
        private static string purgeSql = "truncate table QueueItems";
        private static string databaseSql = "\r\n            if not exists (SELECT 1 FROM INFORMATION_SCHEMA.SYSTEM_TABLES WHERE TABLE_SCHEM='PUBLIC' AND TABLE_NAME='QUEUEITEMS')\r\n            then\r\n                create cached table QueueItems (Id int generated by default as identity(start with 1), Payload varchar(102400), EnqueueTime timestamp, Priority bigint, Try int, State tinyint)\r\n            end if;\r\n            if not exists (SELECT 1 FROM INFORMATION_SCHEMA.SYSTEM_TABLES WHERE TABLE_SCHEM='PUBLIC' AND TABLE_NAME='DISCARDQUEUEITEMS')\r\n            then\r\n                create cached table DiscardQueueItems (Id int, Payload varchar(102400), EnqueueTime timestamp)\r\n            end if;";
        private static string restoreAndCountSql = "update QueueItems set State = 0; select count(1) from QueueItems;";
        private static string connectionStringFormat = "Connection Type=File; Initial Catalog={0}; User=sa; Password=; Pooling=true; Connect Timeout=200; Auto Commit=true";

        public EfzQueuePersistence(string persistPath)
            : base(persistPath)
        {
        }

        protected override void MultiSaveImpl(params PersistentQueueItem<T>[] items)
        {
            using (EfzConnection connection = new EfzConnection(this.connString))
            {
                StringBuilder sql = new StringBuilder();
                for (int i = 0; i < items.Length; i++)
                {
                    PersistentQueueItem<T> item = items[i];
                    sql.Append(string.Format("insert into QueueItems(Payload, EnqueueTime, Priority, Try, State) values('{0}', '{1}', {2}, 0, 0);", item.PayloadToJson().Replace("'", "''"), item.EnqueueTime.ToString("yyyy-MM-dd HH:mm:ss"), item.Priority));
                }
                EfzCommand command = connection.CreateCommand();
                command.CommandText = sql.ToString();
                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        protected override List<PersistentQueueItem<T>> MultiLoadImpl(int batch)
        {
            List<PersistentQueueItem<T>> result = new List<PersistentQueueItem<T>>(batch);
            using (EfzConnection connection = new EfzConnection(this.connString))
            {
                EfzCommand command = connection.CreateCommand();
                command.CommandText = "select top " + batch + " Id, Payload, EnqueueTime, Priority, Try from QueueItems where State = 0 order by Priority";
                connection.Open();
                using (EfzDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(PersistentQueueItem<T>.FromDataReader(reader));
                    }
                }
                if (result.Count > 0)
                {
                    StringBuilder ids = new StringBuilder(result[0].Id.ToString());
                    for (int i = 1; i < result.Count; i++)
                    {
                        ids.Append(",");
                        ids.Append(result[i].Id);
                    }
                    command.CommandText = "update QueueItems set State = 1 where Id in (" + ids.ToString() + ");";
                    command.ExecuteNonQuery();
                }
            }
            return result;
        }

        protected override void RemoveImpl(PersistentQueueItem<T> item)
        {
            using (EfzConnection connection = new EfzConnection(this.connString))
            {
                EfzCommand command = connection.CreateCommand();
                command.CommandText = "delete from QueueItems where Id = " + item.Id;
                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        protected override void FailImpl(PersistentQueueItem<T> item)
        {
            using (EfzConnection connection = new EfzConnection(this.connString))
            {
                EfzCommand command = connection.CreateCommand();
                command.CommandText = string.Format("update QueueItems set Priority = {0}, Try = {1}, State = 0 where Id = {2}", item.Priority, item.Try, item.Id);
                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        protected override void DiscardImpl(PersistentQueueItem<T> item)
        {
            using (EfzConnection connection = new EfzConnection(this.connString))
            {
                string sql = string.Format("delete from QueueItems where Id = {0}; insert into DiscardQueueItems(Id, Payload, EnqueueTime) values({0}, '{1}', '{2}');", item.Id, item.PayloadToJson(), item.EnqueueTime.ToString("yyyy-MM-dd HH:mm:ss"));
                EfzCommand command = connection.CreateCommand();
                command.CommandText = sql;
                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        protected override void PurgeImpl()
        {
            using (EfzConnection connection = new EfzConnection(this.connString))
            {
                EfzCommand command = connection.CreateCommand();
                command.CommandText = EfzQueuePersistence<T>.purgeSql;
                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        protected override int Init(string persistPath)
        {
            this.connString = EfzQueuePersistence<T>.GetConnectionString(persistPath);
            int result;
            using (EfzConnection connection = new EfzConnection(this.connString))
            {
                EfzCommand command = connection.CreateCommand();
                command.CommandText = EfzQueuePersistence<T>.databaseSql;
                connection.Open();
                command.ExecuteNonQuery();
                command.CommandText = EfzQueuePersistence<T>.restoreAndCountSql;
                result = (int)command.ExecuteScalar();
            }
            return result;
        }

        private static string GetConnectionString(string path)
        {
            return string.Format(EfzQueuePersistence<T>.connectionStringFormat, path);
        }
    }
}