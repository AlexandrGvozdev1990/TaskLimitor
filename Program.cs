using System;

namespace TaskLimitor
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    class Program
    {
        /// <summary>Операция, которая будет выполняться в контексте вторичных потоков</summary>
        static void Operation()
        {
            Thread.Sleep(3000);
            
            Console.WriteLine($"Задача {Task.CurrentId} выполняется в потоке {Thread.CurrentThread.ManagedThreadId}");
        }
        
        static void Main(string[] args)
        {
            // создаем экземпляр планировщика
            var limitedTaskScheduler = new LimitedTaskScheduler(5, 1000);
            
            // создаем экземпляр фабрики задачи и конфигурирем ее нашим планировщиком
            var taskFactory = new TaskFactory(limitedTaskScheduler);
            
            List<Task> tasks = new List<Task>();

            // цикл запуска 20 задач
            for(int i = 0; i < 20; i++)
            {
                tasks.Add(taskFactory.StartNew(Operation));
            }

            // ожидание завершения всех задач
            Task.WaitAll(tasks.ToArray());

            Console.ReadKey();
        }
    }
}
