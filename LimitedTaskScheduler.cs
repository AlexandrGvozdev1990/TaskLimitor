namespace TaskLimitor
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel.Design;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    ///     Планировщик ограниченного числа задач. Одновременно может выполняться столько задач,
    ///     сколько укажет пользователь при создании экземпляра планировщика
    /// </summary>
    public class LimitedTaskScheduler : TaskScheduler, IDisposable
    {
        /// <summary>Максимально возможное число задач, которое может выполнять планировщик</summary>
        private readonly int _maxAllowTasksCount;

        /// <summary>
        ///     Временной интервал, по истечению которого будет происходить
        ///     проверка возможности добавления одижающих задач в очередь на выполнение
        /// </summary>
        private readonly uint _interval;
        
        /// <summary>Число задач, выполняющихся в данных момент</summary>
        private uint _executingTasksCount;
        
        /// <summary>Очередь задач на выполнение</summary>
        private readonly List<Task> _taskQueue;

        /// <summary>Коллекция задач, которые ожидают постановки в очередь на выполнение</summary>
        /// <remarks>Если очередь </remarks>
        private readonly Queue<Task> _pendingTasks = new Queue<Task>();

        /// <summary>Таймер, который через определенные интервалы времени будет удалять из списка завершенные задачи и ставить новые</summary>
        private readonly Timer _timer;
        
        /// <summary>Объект синхронизации</summary>
        private object _syncObject = new object();
        
        /// <summary>Конструктор</summary>
        /// <param name="maxAllowTasksCount">Максимально возможное число задач, которое может выполнять планировщик</param>
        /// <param name="interval">
        ///     Временной интервал, по истечению которого будет происходить
        ///     проверка возможности добавления одижающих задач в очередь на выполнение
        /// </param>
        public LimitedTaskScheduler(int maxAllowTasksCount, uint interval)
        {
            Validate(maxAllowTasksCount);

            _maxAllowTasksCount = maxAllowTasksCount;

            _interval = interval;
            
            _timer = new Timer(TryQueueTask, null, 0, Timeout.Infinite);
            
            _taskQueue = new List<Task>();
        }
        
        /// <summary>Постановка задачи в очередь</summary>
        /// <param name="task">Задача, которую необходимо поставить в очередь на выполнение</param>
        /// <remarks>Вызывается автоматически планировщиком задач, когда задача создана и готова к выполнению</remarks>
        protected override void QueueTask(Task task)
        {
            lock(_syncObject)
            {
                // проверка, разрешено ли ставить в очередь на выполнение новую задачу
                if(IsPossibleSchedulerTasks())
                {
                    // добавляем задачу в очередь задач для выполнения
                   _taskQueue.Add(task);

                    // увеличиваем счетчик выполняющихся в настоящий момент задач
                    ++_executingTasksCount;

                    ThreadPool.QueueUserWorkItem(state => TryExecuteTask(task));
                }
                else
                {
                    // добавляем задачу в очередь задач для ожидания
                    _pendingTasks.Enqueue(task);
                }
            }
        }

        /// <summary>Попытка выполнить указанную задачу в текущем потоке</summary>
        /// <param name="task">Задача, котрыую необходимо выполнить</param>
        /// <param name="taskWasPreviouslyQueued">Параметр, показывающий была ли задача поставлена на выполнение ранее</param>
        /// <returns>true - если задача была выполнена в текущем потоке, иначе false</returns>
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return false;
        }
        
        /// <summary>Получить коллекцию задач, которые ожидают выполнения</summary>
        /// <returns>Колелкция задач, которые ожидают выполнения</returns>
        /// <remarks>Используется в целях отладки</remarks>
        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return _taskQueue;
        }

        /// <summary>Проверка параметра, указывающего на количество одновременно выполняющихся задач</summary>
        private void Validate(int maxAllowTasksCount)
        {
            if(maxAllowTasksCount < 1)
            {
                throw new ArgumentException("Количество одновременно выполняющихся задач не может быть меньше 1");
            }
        }

        /// <summary>Удаление задач, которые завершились, из списка выполняющихся задач</summary>
        private void RemoveCompletedTasks()
        {
            // получение задач, которые завершились (это могут быть задачи, которые завершились успешно, были отменены, завершились с ошибкой)
            var completedTasks = _taskQueue.Where(task => task.IsCompleted).ToArray();
            
            foreach(var completedTask in completedTasks)
            {
                // удаление завершивнейся задачи из списка
                _taskQueue.Remove(completedTask);
            
                // уменьшаем ан 1 счетчик выполняющихся задач
                --_executingTasksCount;
            }
        }
        
        /// <summary>Попытка отправить задачи из очереди ожидания на выполнение</summary>
        private void TryQueueTask(object _)
        {
            lock(_syncObject)
            {
                // заставляем таймер вызвать снова этот метод через указанный интервал времени
                _timer.Change(_interval, Timeout.Infinite);
                
                // если очередь ожижающих задач пустая, то ставить в очередь на выполнение нечего и выходим из метода
                if(_pendingTasks.Count == 0)
                {
                    return;
                }

                // удаляем завершившиеся задачи из списка выполняющихся задач
                RemoveCompletedTasks();
            
                // проверка, можно ли поставить некоторые задачи на выполнение из очереди ожидания
                if(IsPossibleSchedulerTasks())
                {
                    // постановка нескольких ожидающих задач в очередь на выполение
                    QueueTask();
                }
            }
        }

        /// <summary>Постановка нескольких ожидающих задач в очередь на выполение</summary>
        private void QueueTask()
        {
            // число задач, которые можно поставить в очередь на выполнение
            var allowedToAdd = _maxAllowTasksCount - _executingTasksCount;

            for(int i = 0; i < allowedToAdd; i++)
            {
                // Попытка достать из очереди самую первую задачу и отправить ее на выполнение.
                // Если очередь ожидающих задач пустая, то выходим из цикла 
                if(_pendingTasks.TryDequeue(out var task))
                {
                    QueueTask(task);
                }
                else
                {
                    break;
                }
            }
        }

        /// <summary>Проверка возможности добавления новой задачи в очередь на выполнение</summary>
        /// <returns>true - задача может быть поставлена на выполнение, иначе false</returns>
        private bool IsPossibleSchedulerTasks()
        {
            return _executingTasksCount < _maxAllowTasksCount;
        }
        
        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
