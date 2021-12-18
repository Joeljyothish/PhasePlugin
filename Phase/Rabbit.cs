using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TShockAPI;

namespace Phase
{
    public class Message : EventArgs
    {
        public string content { get; private set; }

        private Message() { }

        public Message(string content)
        {
            this.content = content;
        }
    }

    public class Rabbit : IDisposable
    {
        bool disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    this.NewMessage = null;
                    this.channel.Dispose();
                    this.connection.Dispose();
                    this.factory = null;
                    this.consumer = null;
                    //dispose managed resources
                }
            }
            //dispose unmanaged resources
            disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ConnectionFactory factory;
        IConnection connection;
        IModel channel;
        EventingBasicConsumer consumer;
        public delegate void MessageEventHandler(
       object sender,
       Message args);

        public event MessageEventHandler NewMessage;

        protected virtual void OnNewMessageEntry(string content)
        {
            if (NewMessage != null)
            {
                NewMessage(this, new Message(content));
            }
        }

        public Rabbit(string HostName, string Username, string Password, string vHost)
        {
            try {
                factory = new ConnectionFactory() { HostName = HostName, UserName = Username, Password = Password, VirtualHost = '/' + vHost };
                factory.AutomaticRecoveryEnabled = true;
                connection = factory.CreateConnection();
                channel = connection.CreateModel();
                SetupQueues();
            } catch(Exception e)
            {
                TShock.Log.ConsoleError(e.Message);
            }
        }

        private void SetupQueues()
        {
            channel.ExchangeDeclarePassive(exchange: "phase_in");
            channel.ExchangeDeclarePassive(exchange: "phase_out_"+ Phase.Config.exchangeName);

            var queueName = channel.QueueDeclare().QueueName;
            channel.QueueBind(queue: queueName,
                              exchange: "phase_out_" + Phase.Config.exchangeName,
                              routingKey: "");

            consumer = new EventingBasicConsumer(channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body;
                var message = Encoding.UTF8.GetString(body);
                OnNewMessageEntry(message);
                //Console.WriteLine(" [x] {0}", message);
            };

            channel.BasicConsume(queue: queueName,
                                 consumer: consumer,
                                 autoAck: true);
        }

        public void Publish(string message)
        {
            try {
                var body = Encoding.UTF8.GetBytes(message);
                channel.BasicPublish(exchange: "phase_in",
                                         routingKey: "",
                                         basicProperties: null,
                                         body: body);
            }
            catch (Exception e)
            {
                TShock.Log.ConsoleError(e.Message);
            }
        }
    }
}
