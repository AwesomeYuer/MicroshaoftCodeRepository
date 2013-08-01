namespace Test
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microshaoft;
    class Program
    {
        static void Main()
        {
            GCNotifier.RegisterForFullGCNotification
                                (
                                    99
                                    , 99
                                    , 10
                                    , (x) =>
                                    {
                                        //if (x != GCNotificationStatus.Timeout)
                                        {
                                            Console.WriteLine("FullGCApproach {0}", x);
                                        }
                                    }
                                    , (x) =>
                                    {
                                        //if (x != GCNotificationStatus.Timeout)
                                        {
                                            Console.WriteLine("FullGCComplete {0}", x);
                                        }
                                    }
                                );
            var q = new ConcurrentAsyncQueue<int>();
            q.AttachPerformanceCounters
                                (
                                    "new"
                                    , "Microshaoft ConcurrentAsyncQueue Performance Counters"
                                    , new PerformanceCountersContainer()
                                 );
            Random random = new Random();
            q.OnDequeue += new ConcurrentAsyncQueue<int>.QueueEventHandler
                                                    (
                                                        (x) =>
                                                        {
                                                            int sleep = random.Next(0, 9) * 50;
                                                            //Console.WriteLine(sleep);
                                                            //Thread.Sleep(sleep);
                                                            if (sleep > 400)
                                                            {
                                                                Console.WriteLine(x);
                                                            }
                                                        }
                                                    );
            q.OnException += new ConcurrentAsyncQueue<int>.ExceptionEventHandler
                                                                    (
                                                                        (x) =>
                                                                        {
                                                                            Console.WriteLine(x.ToString());
                                                                        }
                                                                    );
            Console.WriteLine("begin ...");
            //q.StartAdd(10);
            string r = string.Empty;
            while ((r = Console.ReadLine()) != "q")
            {
                int i;
                if (int.TryParse(r, out i))
                {
                    Console.WriteLine("Parallel Enqueue {0} begin ...", i);
                    new Thread
                            (
                                new ParameterizedThreadStart
                                            (
                                                (x) =>
                                                {
                                                    Parallel.For
                                                                (
                                                                    0
                                                                    , i
                                                                    , (xx) =>
                                                                    {
                                                                        q.Enqueue(xx);
                                                                    }
                                                                );
                                                    Console.WriteLine("Parallel Enqueue {0} end ...", i);
                                                }
                                            )
                            ).Start();
                }
                else if (r.ToLower() == "stop")
                {
                    q.StartStop(10);
                }
                else if (r.ToLower() == "add")
                {
                    q.StartAdd(20);
                }
                else
                {
                    Console.WriteLine("please input Number!");
                }
            }
        }
    }
}
namespace Microshaoft
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Threading;
    public class ConcurrentAsyncQueue<T>
    {
        public delegate void QueueEventHandler(T item);
        public event QueueEventHandler OnDequeue;
        public delegate void QueueLogEventHandler(string logMessage);
        public QueueLogEventHandler
                                OnQueueLog
                                , OnDequeueThreadStart
                                , OnDequeueThreadEnd;
        public delegate void ExceptionEventHandler(Exception exception);
        public event ExceptionEventHandler OnException;
        private ConcurrentQueue<Tuple<Stopwatch, T>> _queue =
                                    new ConcurrentQueue<Tuple<Stopwatch, T>>();
        private ConcurrentQueue<Action> _callbackProcessBreaksActions;
        private long _concurrentDequeueThreadsCount = 0; //Microshaoft 用于控制并发线程数
        private ConcurrentQueue<ThreadProcessor> _dequeueThreadsProcessorsPool;
        private int _dequeueIdleSleepSeconds = 10;
        public PerformanceCountersContainer PerformanceCounters
        {
            get;
            private set;
        }
        public int DequeueIdleSleepSeconds
        {
            set
            {
                _dequeueIdleSleepSeconds = value;
            }
            get
            {
                return _dequeueIdleSleepSeconds;
            }
        }
        private bool _isAttachedPerformanceCounters = false;
        private class ThreadProcessor
        {
            public bool Break
            {
                set;
                get;
            }
            public EventWaitHandle Wait
            {
                private set;
                get;
            }
            public ConcurrentAsyncQueue<T> Sender
            {
                private set;
                get;
            }
            public void StopOne()
            {
                Break = true;
            }
            public ThreadProcessor
                            (
                                ConcurrentAsyncQueue<T> queue
                                , EventWaitHandle wait
                            )
            {
                Wait = wait;
                Sender = queue;
            }
            public void ThreadProcess()
            {
                Interlocked.Increment(ref Sender._concurrentDequeueThreadsCount);
                bool counterEnabled = Sender._isAttachedPerformanceCounters;
                if (counterEnabled)
                {
                    Sender.PerformanceCounters.DequeueThreadStartPerformanceCounter.ChangeCounterValueWithTryCatchExceptionFinally<long>
                                                (
                                                    counterEnabled
                                                    , (x) =>
                                                    {
                                                        return x.Increment();
                                                    }
                                                );
                    Sender.PerformanceCounters.DequeueThreadsCountPerformanceCounter.Increment();
                }
                long r = 0;
                try
                {
                    if (Sender.OnDequeueThreadStart != null)
                    {
                        r = Interlocked.Read(ref Sender._concurrentDequeueThreadsCount);
                        Sender.OnDequeueThreadStart
                                        (
                                            string.Format
                                                    (
                                                        "{0} Threads Count {1},Queue Count {2},Current Thread: {3} at {4}"
                                                        , "Threads ++ !"
                                                        , r
                                                        , Sender._queue.Count
                                                        , Thread.CurrentThread.Name
                                                        , DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fffff")
                                                    )
                                        );
                    }
                    while (true)
                    {
                        #region while true loop
                        if (Break)
                        {
                            break;
                        }
                        while (!Sender._queue.IsEmpty)
                        {
                            #region while queue.IsEmpty loop
                            if (Break)
                            {
                                break;
                            }
                            Tuple<Stopwatch, T> item = null;
                            if (Sender._queue.TryDequeue(out item))
                            {
                                if (counterEnabled)
                                {
                                    Sender.PerformanceCounters.DequeuePerformanceCounter.Increment();
                                    Sender.PerformanceCounters.QueueLengthPerformanceCounter.Decrement();
                                }
                                if (Sender.OnDequeue != null)
                                {
                                    if (counterEnabled)
                                    {
                                        var beginTimeStopwatch = item.Item1;
                                        if
                                            (
                                                beginTimeStopwatch != null
                                            )
                                        {
                                            beginTimeStopwatch.Stop();
                                            long elapsedTicks = beginTimeStopwatch.ElapsedTicks;
                                            Sender.PerformanceCounters.QueuedWaitAverageTimerPerformanceCounter.IncrementBy(elapsedTicks);
                                            Sender.PerformanceCounters.QueuedWaitAverageBasePerformanceCounter.Increment();
                                        }
                                    }
                                    var element = item.Item2;
                                    item = null;
                                    Sender.PerformanceCounters.DequeueProcessedAverageTimerPerformanceCounter.ChangeAverageTimerCounterValueWithTryCatchExceptionFinally
                                            (
                                                counterEnabled
                                                , Sender.PerformanceCounters.DequeueProcessedAverageBasePerformanceCounter
                                                , () =>
                                                {
                                                    Sender.OnDequeue(element);
                                                }
                                            );
                                }
                                if (Sender._isAttachedPerformanceCounters)
                                {
                                    Sender.PerformanceCounters.DequeueProcessedPerformanceCounter.Increment();
                                    Sender.PerformanceCounters.DequeueProcessedRateOfCountsPerSecondPerformanceCounter.Increment();
                                }
                            }
                            #endregion while queue.IsEmpty loop
                        }
                        #region wait
                        Sender._dequeueThreadsProcessorsPool.Enqueue(this);
                        if (Break)
                        {
                        }
                        if (!Wait.WaitOne(Sender.DequeueIdleSleepSeconds * 1000))
                        {
                        }
                        #endregion wait
                        #endregion while true loop
                    }
                }
                catch (Exception e)
                {
                    if (Sender.OnException != null)
                    {
                        Sender.OnException(e);
                    }
                }
                finally
                {
                    r = Interlocked.Decrement(ref Sender._concurrentDequeueThreadsCount);
                    if (r < 0)
                    {
                        Interlocked.Exchange(ref Sender._concurrentDequeueThreadsCount, 0);
                        if (Sender._isAttachedPerformanceCounters)
                        {
                            if (Sender.PerformanceCounters.DequeueThreadsCountPerformanceCounter.RawValue < 0)
                            {
                                Sender.PerformanceCounters.DequeueThreadsCountPerformanceCounter.RawValue = Sender._concurrentDequeueThreadsCount;
                            }
                        }
                    }
                    if (Sender.OnDequeueThreadEnd != null)
                    {
                        Sender.OnDequeueThreadEnd
                                    (
                                        string.Format
                                                (
                                                    "{0} Threads Count {1},Queue Count {2},Current Thread: {3} at {4}"
                                                    , "Threads--"
                                                    , r
                                                    , Sender._queue.Count
                                                    , Thread.CurrentThread.Name
                                                    , DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fffff")
                                                )
                                    );
                    }
                    if (Sender._isAttachedPerformanceCounters)
                    {
                        Sender.PerformanceCounters.DequeueThreadEndPerformanceCounter.Increment();
                        Sender.PerformanceCounters.DequeueThreadsCountPerformanceCounter.Decrement();
                    }
                    if (!Break)
                    {
                        Sender.StartAdd(1);
                    }
                    Break = false;
                }
            }
        }
        public void AttachPerformanceCounters
                            (
                                string instanceNamePrefix
                                , string categoryName
                                , PerformanceCountersContainer performanceCounters
                            )
        {
            var process = Process.GetCurrentProcess();
            var processName = process.ProcessName;
            var instanceName = string.Format
                                    (
                                        "{0}-{1}"
                                        , instanceNamePrefix
                                        , processName
                                    );
            PerformanceCounters = performanceCounters;
            PerformanceCounters.AttachPerformanceCountersToProperties(instanceName, categoryName);
            _isAttachedPerformanceCounters = true;
        }
        public int Count
        {
            get
            {
                return _queue.Count;
            }
        }
        public long ConcurrentThreadsCount
        {
            get
            {
                return _concurrentDequeueThreadsCount;
            }
        }
        private void Stop(int count)
        {
            Action action;
            for (var i = 0; i < count; i++)
            {
                if (_callbackProcessBreaksActions.TryDequeue(out action))
                {
                    action();
                    action = null;
                }
            }
        }
        public void StartStop(int count)
        {
            new Thread
                    (
                        new ThreadStart
                                (
                                    () =>
                                    {
                                        Stop(count);
                                    }
                                )
                    ).Start();
        }
        public void StartAdd(int count)
        {
            new Thread
                    (
                        new ThreadStart
                                (
                                    () =>
                                    {
                                        Add(count);
                                    }
                                )
                    ).Start();
        }
        private void Add(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Interlocked.Increment(ref _concurrentDequeueThreadsCount);
                if (_dequeueThreadsProcessorsPool == null)
                {
                    _dequeueThreadsProcessorsPool = new ConcurrentQueue<ThreadProcessor>();
                }
                var processor = new ThreadProcessor
                                                (
                                                    this
                                                    , new AutoResetEvent(false)
                                                );
                var thread = new Thread
                                    (
                                        new ThreadStart
                                                    (
                                                        processor.ThreadProcess
                                                    )
                                    );
                if (_callbackProcessBreaksActions == null)
                {
                    _callbackProcessBreaksActions = new ConcurrentQueue<Action>();
                }
                var callbackProcessBreakAction = new Action
                                                        (
                                                            processor.StopOne
                                                        );
                _callbackProcessBreaksActions.Enqueue(callbackProcessBreakAction);
                _dequeueThreadsProcessorsPool.Enqueue(processor);
                thread.Start();
            }
        }
        public void Enqueue(T item)
        {
            try
            {
                Stopwatch stopwatch = null;
                if (_isAttachedPerformanceCounters)
                {
                    stopwatch = Stopwatch.StartNew();
                }
                var element = Tuple.Create<Stopwatch, T>(stopwatch, item);
                _queue.Enqueue(element);
                if (_isAttachedPerformanceCounters)
                {
                    PerformanceCounters.EnqueuePerformanceCounter.Increment();
                    PerformanceCounters.EnqueueRateOfCountsPerSecondPerformanceCounter.Increment();
                    PerformanceCounters.QueueLengthPerformanceCounter.Increment();
                }
                if
                    (
                        _dequeueThreadsProcessorsPool != null
                        && !_dequeueThreadsProcessorsPool.IsEmpty
                    )
                {
                    ThreadProcessor processor;
                    if (_dequeueThreadsProcessorsPool.TryDequeue(out processor))
                    {
                        processor.Wait.Set();
                        processor = null;
                        //Console.WriteLine("processor = null;");
                    }
                }
            }
            catch (Exception e)
            {
                if (OnException != null)
                {
                    OnException(e);
                }
            }
        }
    }
}
namespace Microshaoft
{
    using System.Diagnostics;
    public static class PerformanceCounterHelper
    {
        public static CounterCreationData GetCounterCreationData(string counterName, PerformanceCounterType performanceCounterType)
        {
            return new CounterCreationData()
            {
                CounterName = counterName
                ,
                CounterHelp = string.Format("{0} Help", counterName)
                ,
                CounterType = performanceCounterType
            };
        }
    }
}
namespace Microshaoft
{
    using System;
    using System.Diagnostics;
    public static class PerformanceCounterExtensionMethodsManager
    {
        public static T ChangeCounterValueWithTryCatchExceptionFinally<T>
                                (
                                    this PerformanceCounter performanceCounter
                                    , bool enabled
                                    , Func<PerformanceCounter, T> OnCounterChangeProcessFunc = null
                                    , Action<PerformanceCounter> OnCounterChangedProcessAction = null
                                    , Func<PerformanceCounter, Exception, bool> OnCatchedExceptionProcessFunc = null
                                    , Action<PerformanceCounter> OnCatchedExceptionFinallyProcessAction = null
                                )
        {
            T r = default(T);
            if (enabled)
            {
                if (OnCounterChangeProcessFunc != null)
                {
                    var catchedException = false;
                    try
                    {
                        r = OnCounterChangeProcessFunc(performanceCounter);
                    }
                    catch (Exception e)
                    {
                        catchedException = true;
                        if (OnCatchedExceptionProcessFunc != null)
                        {
                            var b = OnCatchedExceptionProcessFunc(performanceCounter, e);
                            if (b)
                            {
                                throw new Exception("OnCounterChangeProcessFunc InnerExcepion", e);
                            }
                        }
                    }
                    finally
                    {
                        if (catchedException)
                        {
                            if (OnCatchedExceptionFinallyProcessAction != null)
                            {
                                OnCatchedExceptionFinallyProcessAction(performanceCounter);
                            }
                        }
                    }
                }
            }
            if (OnCounterChangedProcessAction != null)
            {
                var catchedException = false;
                try
                {
                    OnCounterChangedProcessAction(performanceCounter);
                }
                catch (Exception e)
                {
                    catchedException = true;
                    if (OnCatchedExceptionProcessFunc != null)
                    {
                        var b = OnCatchedExceptionProcessFunc(performanceCounter, e);
                        if (b)
                        {
                            throw new Exception("OnCounterChangedProcessAction InnerExcepion", e);
                        }
                    }
                }
                finally
                {
                    if (catchedException)
                    {
                        if (OnCatchedExceptionFinallyProcessAction != null)
                        {
                            OnCatchedExceptionFinallyProcessAction(performanceCounter);
                        }
                    }
                }
            }
            return r;
        }
        public static void ChangeAverageTimerCounterValueWithTryCatchExceptionFinally
                                                        (
                                                            this PerformanceCounter performanceCounter
                                                            , bool enabled
                                                            , PerformanceCounter basePerformanceCounter
                                                            , Action OnCounterInnerProcessAction = null
                                                            , Func<PerformanceCounter, Exception, bool> OnCatchedExceptionProcessFunc = null
                                                            , Action<PerformanceCounter, PerformanceCounter> OnCatchedExceptionFinallyProcessAction = null
                                                        )
        {
            if (enabled)
            {
                var stopwatch = Stopwatch.StartNew();
                if (OnCounterInnerProcessAction != null)
                {
                    var catchedException = false;
                    try
                    {
                        OnCounterInnerProcessAction();
                    }
                    catch (Exception e)
                    {
                        catchedException = true;
                        if (OnCatchedExceptionProcessFunc != null)
                        {
                            var b = OnCatchedExceptionProcessFunc(performanceCounter, e);
                            if (b)
                            {
                                throw new Exception("OnCounterInnerProcessAction InnerExcepion", e);
                            }
                        }
                    }
                    finally
                    {
                        stopwatch.Stop();
                        performanceCounter.IncrementBy(stopwatch.ElapsedTicks);
                        stopwatch = null;
                        basePerformanceCounter.Increment();
                        if (catchedException)
                        {
                            if (OnCatchedExceptionFinallyProcessAction != null)
                            {
                                OnCatchedExceptionFinallyProcessAction(performanceCounter, basePerformanceCounter);
                            }
                        }
                    }
                }
            }
        }
    }
}
namespace Microshaoft
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Collections.Concurrent;
    public class PerformanceCountersContainer
    {
        #region PerformanceCounters
        private PerformanceCounter _enqueuePerformanceCounter;
        [PerformanceCounterTypeAttribute(CounterType = PerformanceCounterType.NumberOfItems64)]
        public PerformanceCounter EnqueuePerformanceCounter
        {
            private set
            {
                ReaderWriterLockSlimHelper.TryEnterWriterLockSlimWrite<PerformanceCounter>(ref _enqueuePerformanceCounter, value, 2);
            }
            get
            {
                return _enqueuePerformanceCounter;
            }
        }
        private PerformanceCounter _enqueueRateOfCountsPerSecondPerformanceCounter;
        [PerformanceCounterTypeAttribute(CounterType = PerformanceCounterType.RateOfCountsPerSecond64)]
        public PerformanceCounter EnqueueRateOfCountsPerSecondPerformanceCounter
        {
            private set
            {
                ReaderWriterLockSlimHelper.TryEnterWriterLockSlimWrite<PerformanceCounter>(ref _enqueueRateOfCountsPerSecondPerformanceCounter, value, 2);
            }
            get
            {
                return _enqueueRateOfCountsPerSecondPerformanceCounter;
            }
        }
        private PerformanceCounter _dequeuePerformanceCounter;
        [PerformanceCounterTypeAttribute(CounterType = PerformanceCounterType.NumberOfItems64)]
        public PerformanceCounter DequeuePerformanceCounter
        {
            private set
            {
                ReaderWriterLockSlimHelper.TryEnterWriterLockSlimWrite<PerformanceCounter>(ref _dequeuePerformanceCounter, value, 2);
            }
            get
            {
                return _dequeuePerformanceCounter;
            }
        }
        private PerformanceCounter _dequeueProcessedRateOfCountsPerSecondPerformanceCounter;
        [PerformanceCounterTypeAttribute(CounterType = PerformanceCounterType.RateOfCountsPerSecond64)]
        public PerformanceCounter DequeueProcessedRateOfCountsPerSecondPerformanceCounter
        {
            private set
            {
                ReaderWriterLockSlimHelper.TryEnterWriterLockSlimWrite<PerformanceCounter>(ref _dequeueProcessedRateOfCountsPerSecondPerformanceCounter, value, 2);
            }
            get
            {
                return _dequeueProcessedRateOfCountsPerSecondPerformanceCounter;
            }
        }
        private PerformanceCounter _dequeueProcessedPerformanceCounter;
        [PerformanceCounterTypeAttribute(CounterType = PerformanceCounterType.NumberOfItems64)]
        public PerformanceCounter DequeueProcessedPerformanceCounter
        {
            private set
            {
                ReaderWriterLockSlimHelper.TryEnterWriterLockSlimWrite<PerformanceCounter>(ref _dequeueProcessedPerformanceCounter, value, 2);
            }
            get
            {
                return _dequeueProcessedPerformanceCounter;
            }
        }
        private PerformanceCounter _dequeueProcessedAverageTimerPerformanceCounter;
        [PerformanceCounterTypeAttribute(CounterType = PerformanceCounterType.AverageTimer32)]
        public PerformanceCounter DequeueProcessedAverageTimerPerformanceCounter
        {
            private set
            {
                ReaderWriterLockSlimHelper.TryEnterWriterLockSlimWrite<PerformanceCounter>(ref _dequeueProcessedAverageTimerPerformanceCounter, value, 2);
            }
            get
            {
                return _dequeueProcessedAverageTimerPerformanceCounter;
            }
        }
        private PerformanceCounter _dequeueProcessedAverageBasePerformanceCounter;
        [PerformanceCounterTypeAttribute(CounterType = PerformanceCounterType.AverageBase)]
        public PerformanceCounter DequeueProcessedAverageBasePerformanceCounter
        {
            private set
            {
                ReaderWriterLockSlimHelper.TryEnterWriterLockSlimWrite<PerformanceCounter>(ref _dequeueProcessedAverageBasePerformanceCounter, value, 2);
            }
            get
            {
                return _dequeueProcessedAverageBasePerformanceCounter;
            }
        }
        private PerformanceCounter _queuedWaitAverageTimerPerformanceCounter;
        [PerformanceCounterTypeAttribute(CounterType = PerformanceCounterType.AverageTimer32)]
        public PerformanceCounter QueuedWaitAverageTimerPerformanceCounter
        {
            private set
            {
                ReaderWriterLockSlimHelper.TryEnterWriterLockSlimWrite<PerformanceCounter>(ref _queuedWaitAverageTimerPerformanceCounter, value, 2);
            }
            get
            {
                return _queuedWaitAverageTimerPerformanceCounter;
            }
        }
        private PerformanceCounter _queuedWaitAverageBasePerformanceCounter;
        [PerformanceCounterTypeAttribute(CounterType = PerformanceCounterType.AverageBase)]
        public PerformanceCounter QueuedWaitAverageBasePerformanceCounter
        {
            private set
            {
                ReaderWriterLockSlimHelper.TryEnterWriterLockSlimWrite<PerformanceCounter>(ref _queuedWaitAverageBasePerformanceCounter, value, 2);
            }
            get
            {
                return _queuedWaitAverageBasePerformanceCounter;
            }
        }
        private PerformanceCounter _queueLengthPerformanceCounter;
        [PerformanceCounterTypeAttribute(CounterType = PerformanceCounterType.NumberOfItems64)]
        public PerformanceCounter QueueLengthPerformanceCounter
        {
            private set
            {
                ReaderWriterLockSlimHelper.TryEnterWriterLockSlimWrite<PerformanceCounter>(ref _queueLengthPerformanceCounter, value, 2);
            }
            get
            {
                return _queueLengthPerformanceCounter;
            }
        }
        private PerformanceCounter _dequeueThreadStartPerformanceCounter;
        [PerformanceCounterTypeAttribute(CounterType = PerformanceCounterType.NumberOfItems64)]
        public PerformanceCounter DequeueThreadStartPerformanceCounter
        {
            private set
            {
                ReaderWriterLockSlimHelper.TryEnterWriterLockSlimWrite<PerformanceCounter>(ref _dequeueThreadStartPerformanceCounter, value, 2);
            }
            get
            {
                return _dequeueThreadStartPerformanceCounter;
            }
        }
        private PerformanceCounter _dequeueThreadEndPerformanceCounter;
        [PerformanceCounterTypeAttribute(CounterType = PerformanceCounterType.NumberOfItems64)]
        public PerformanceCounter DequeueThreadEndPerformanceCounter
        {
            private set
            {
                ReaderWriterLockSlimHelper.TryEnterWriterLockSlimWrite<PerformanceCounter>(ref _dequeueThreadEndPerformanceCounter, value, 2);
            }
            get
            {
                return _dequeueThreadEndPerformanceCounter;
            }
        }
        private PerformanceCounter _dequeueThreadsCountPerformanceCounter;
        [PerformanceCounterTypeAttribute(CounterType = PerformanceCounterType.NumberOfItems64)]
        public PerformanceCounter DequeueThreadsCountPerformanceCounter
        {
            private set
            {
                ReaderWriterLockSlimHelper.TryEnterWriterLockSlimWrite<PerformanceCounter>(ref _dequeueThreadsCountPerformanceCounter, value, 2);
            }
            get
            {
                return _dequeueThreadsCountPerformanceCounter;
            }
        }
        #endregion
        // indexer declaration
        public PerformanceCounter this[string name]
        {
            get
            {
                throw new NotImplementedException();
                //return null;
            }
        }
        private bool _isAttachedPerformanceCounters = false;
        public void AttachPerformanceCountersToProperties
                            (
                                string instanceName
                                , string categoryName
                            )
        {
            if (!_isAttachedPerformanceCounters)
            {
                var type = this.GetType();
                PerformanceCountersHelper.AttachPerformanceCountersToProperties<PerformanceCountersContainer>(instanceName, categoryName, this);
            }
            _isAttachedPerformanceCounters = true;
        }
    }
}
namespace Microshaoft
{
    using System;
    using System.Diagnostics;
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class PerformanceCounterTypeAttribute : Attribute
    {
        public PerformanceCounterType CounterType;
    }
}
namespace Microshaoft
{
    using System.Diagnostics;
    using System.Linq;
    public static class PerformanceCountersHelper
    {
        public static void AttachPerformanceCountersToProperties<T>
                                    (
                                        string performanceCounterInstanceName
                                        , string category
                                        , T target = default(T)
                                    )
        {
            var type = typeof(T);
            var propertiesList = type.GetProperties().ToList();
            propertiesList = propertiesList.Where
                                                (
                                                    (pi) =>
                                                    {
                                                        var parameters = pi.GetIndexParameters();
                                                        return
                                                            (
                                                                pi.PropertyType == typeof(PerformanceCounter)
                                                                && (parameters == null ? 0 : parameters.Length) <= 0
                                                            );
                                                    }
                                                ).ToList();
            if (PerformanceCounterCategory.Exists(category))
            {
                propertiesList.ForEach
                                    (
                                        (pi) =>
                                        {
                                            if (PerformanceCounterCategory.CounterExists(pi.Name, category))
                                            {
                                                if (PerformanceCounterCategory.InstanceExists(performanceCounterInstanceName, category))
                                                {
                                                    //var pc = new PerformanceCounter(category, pi.Name, instanceName, false);
                                                    //pc.InstanceName = instanceName;
                                                    //pc.RemoveInstance();
                                                }
                                            }
                                        }
                                    );
                PerformanceCounterCategory.Delete(category);
            }
            var ccdc = new CounterCreationDataCollection();
            propertiesList.ForEach
                            (
                                (pi) =>
                                {
                                    var propertyName = pi.Name;
                                    PerformanceCounterTypeAttribute attribute = pi.GetCustomAttributes(false).FirstOrDefault
                                                                                (
                                                                                    (x) =>
                                                                                    {
                                                                                        return x as PerformanceCounterTypeAttribute != null;
                                                                                    }
                                                                                ) as PerformanceCounterTypeAttribute;
                                    PerformanceCounterType performanceCounterType = (attribute == null ? PerformanceCounterType.NumberOfItems64 : attribute.CounterType);
                                    var ccd = PerformanceCounterHelper.GetCounterCreationData
                                    (
                                        propertyName
                                        , performanceCounterType
                                    );
                                    ccdc.Add(ccd);
                                }
                            );
            PerformanceCounterCategory.Create
                            (
                                category,
                                string.Format("{0} Category Help.", category),
                                PerformanceCounterCategoryType.MultiInstance,
                                ccdc
                            );
            propertiesList.ForEach
                            (
                                (pi) =>
                                {
                                    var propertyName = pi.Name;
                                    var pc = new PerformanceCounter()
                                    {
                                        CategoryName = category
                                        ,
                                        CounterName = propertyName
                                        ,
                                        InstanceLifetime = PerformanceCounterInstanceLifetime.Process
                                        ,
                                        InstanceName = performanceCounterInstanceName
                                        ,
                                        ReadOnly = false
                                        ,
                                        RawValue = 0
                                    };
                                    if (pi.GetGetMethod().IsStatic)
                                    {
                                        var setter = DynamicPropertyAccessor.CreateSetStaticPropertyValueAction<PerformanceCounter>(type, propertyName);
                                        setter(pc);
                                    }
                                    else
                                    {
                                        if (target != null)
                                        {
                                            var setter = DynamicPropertyAccessor.CreateSetPropertyValueAction<PerformanceCounter>(type, propertyName);
                                            setter(target, pc);
                                        }
                                    }
                                }
                            );
        }
    }
}
namespace Microshaoft
{
    using System;
    using System.Threading;
    public static class ReaderWriterLockSlimHelper
    {
        public static bool TryEnterWriterLockSlimWrite<T>
                                                (
                                                     ref T target
                                                    , T newValue
                                                    , int enterTimeOutSeconds
                                                )
                                                    where T : class
        {
            bool r = false;
            var rwls = new ReaderWriterLockSlim();
            int timeOut = Timeout.Infinite;
            if (enterTimeOutSeconds >= 0)
            {
                timeOut = enterTimeOutSeconds * 1000;
            }
            try
            {
                r = (rwls.TryEnterWriteLock(timeOut));
                if (r)
                {
                    Interlocked.Exchange<T>(ref target, newValue);
                    r = true;
                }
            }
            finally
            {
                if (r)
                {
                    rwls.ExitWriteLock();
                }
            }
            return r;
        }
        public static bool TryEnterWriterLockSlim
                                (
                                    Action action
                                    , int enterTimeOutSeconds
                                )
        {
            bool r = false;
            if (action != null)
            {
                var rwls = new ReaderWriterLockSlim();
                int timeOut = Timeout.Infinite;
                if (enterTimeOutSeconds >= 0)
                {
                    timeOut = enterTimeOutSeconds * 1000;
                }
                try
                {
                    r = (rwls.TryEnterWriteLock(timeOut));
                    if (r)
                    {
                        action();
                        r = true;
                    }
                }
                finally
                {
                    if (r)
                    {
                        rwls.ExitWriteLock();
                    }
                }
            }
            return r;
        }
    }
}
namespace Microshaoft
{
    using System;
    using System.Threading;
    public static class GCNotifier
    {
        public static void CancelForFullGCNotification()
        {
            GC.CancelFullGCNotification();
        }
        public static void RegisterForFullGCNotification
                                    (
                                        int maxGenerationThreshold
                                        , int maxLargeObjectHeapThreshold
                                        , int waitOnceSecondsTimeout
                                        , Action<GCNotificationStatus> waitForFullGCApproachProcessAction
                                        , Action<GCNotificationStatus> waitForFullGCCompleteProcessAction
                                    )
        {
            GC.RegisterForFullGCNotification(maxGenerationThreshold, maxLargeObjectHeapThreshold);
            new Thread
                    (
                        new ThreadStart
                                (
                                    () =>
                                    {
                                        while (true)
                                        {
                                            if (waitForFullGCApproachProcessAction != null)
                                            {
                                                var gcNotificationStatus = GC.WaitForFullGCApproach(1000 * waitOnceSecondsTimeout);
                                                if (gcNotificationStatus != GCNotificationStatus.Timeout)
                                                {
                                                    waitForFullGCApproachProcessAction(gcNotificationStatus);
                                                }
                                            }
                                            if (waitForFullGCApproachProcessAction != null)
                                            {
                                                var gcNotificationStatus = GC.WaitForFullGCComplete(1000 * waitOnceSecondsTimeout);
                                                if (gcNotificationStatus != GCNotificationStatus.Timeout)
                                                {
                                                    waitForFullGCCompleteProcessAction(gcNotificationStatus);
                                                }
                                            }
                                            Thread.Sleep(1000);
                                        }
                                    }
                                )
                        ).Start();
        }
    }
}
namespace Microshaoft
{
    using System;
    using System.Linq;
    using System.Linq.Expressions;
    public class DynamicPropertyAccessor
    {
        public static Func<object, object> CreateGetPropertyValueFunc(string typeName, string propertyName, bool isTypeFromAssembly = false)
        {
            Type type;
            if (isTypeFromAssembly)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().First
                                                                (
                                                                    (a) =>
                                                                    {
                                                                        return a.GetTypes().Any
                                                                                            (
                                                                                                (t) =>
                                                                                                {
                                                                                                    return (t.FullName == typeName);
                                                                                                }
                                                                                            );
                                                                    }
                                                                );
                type = assembly.GetType(typeName);
            }
            else
            {
                type = Type.GetType(typeName);
            }
            return CreateGetPropertyValueFunc(type, propertyName);
        }
        public static Func<object, object> CreateGetPropertyValueFunc(Type type, string propertyName)
        {
            var target = Expression.Parameter(typeof(object));
            var castTarget = Expression.Convert(target, type);
            var getPropertyValue = Expression.Property(castTarget, propertyName);
            var castPropertyValue = Expression.Convert(getPropertyValue, typeof(object));
            var lambda = Expression.Lambda<Func<object, object>>(castPropertyValue, target);
            return lambda.Compile();
        }
        public static Func<object, TProperty> CreateGetPropertyValueFunc<TProperty>(string typeName, string propertyName, bool isTypeFromAssembly = false)
        {
            Type type;
            if (isTypeFromAssembly)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().First
                                                                (
                                                                    (a) =>
                                                                    {
                                                                        return a.GetTypes().Any
                                                                                            (
                                                                                                (t) =>
                                                                                                {
                                                                                                    return (t.FullName == typeName);
                                                                                                }
                                                                                            );
                                                                    }
                                                                );
                type = assembly.GetType(typeName);
            }
            else
            {
                type = Type.GetType(typeName);
            }
            return CreateGetPropertyValueFunc<TProperty>(type, propertyName);
        }
        public static Func<object, TProperty> CreateGetPropertyValueFunc<TProperty>(Type type, string propertyName)
        {
            var target = Expression.Parameter(typeof(object));
            var castTarget = Expression.Convert(target, type);
            var getPropertyValue = Expression.Property(castTarget, propertyName);
            var lambda = Expression.Lambda<Func<object, TProperty>>(getPropertyValue, target);
            return lambda.Compile();
        }
        public static Func<TProperty> CreateGetStaticPropertyValueFunc<TProperty>(string typeName, string propertyName, bool isTypeFromAssembly = false)
        {
            Type type;
            if (isTypeFromAssembly)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().First
                                                                (
                                                                    (a) =>
                                                                    {
                                                                        return a.GetTypes().Any
                                                                                            (
                                                                                                (t) =>
                                                                                                {
                                                                                                    return (t.FullName == typeName);
                                                                                                }
                                                                                            );
                                                                    }
                                                                );
                type = assembly.GetType(typeName);
            }
            else
            {
                type = Type.GetType(typeName);
            }
            return CreateGetStaticPropertyValueFunc<TProperty>(type, propertyName);
        }
        public static Func<TProperty> CreateGetStaticPropertyValueFunc<TProperty>(Type type, string propertyName)
        {
            var property = type.GetProperty(propertyName, typeof(TProperty));
            var getPropertyValue = Expression.Property(null, property);
            var lambda = Expression.Lambda<Func<TProperty>>(getPropertyValue, null);
            return lambda.Compile();
        }
        public static Func<object> CreateGetStaticPropertyValueFunc(Type type, string propertyName)
        {
            var property = type.GetProperty(propertyName);
            var getPropertyValue = Expression.Property(null, property);
            var castPropertyValue = Expression.Convert(getPropertyValue, typeof(object));
            var lambda = Expression.Lambda<Func<object>>(castPropertyValue, null);
            return lambda.Compile();
        }
        public static Func<object> CreateGetStaticPropertyValueFunc(string typeName, string propertyName, bool isTypeFromAssembly = false)
        {
            Type type;
            if (isTypeFromAssembly)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().First
                                                                (
                                                                    (a) =>
                                                                    {
                                                                        return a.GetTypes().Any
                                                                                            (
                                                                                                (t) =>
                                                                                                {
                                                                                                    return (t.FullName == typeName);
                                                                                                }
                                                                                            );
                                                                    }
                                                                );
                type = assembly.GetType(typeName);
            }
            else
            {
                type = Type.GetType(typeName);
            }
            return CreateGetStaticPropertyValueFunc(type, propertyName);
        }
        public static Action<object, object> CreateSetPropertyValueAction(Type type, string propertyName)
        {
            var property = type.GetProperty(propertyName);
            var target = Expression.Parameter(typeof(object));
            var propertyValue = Expression.Parameter(typeof(object));
            var castTarget = Expression.Convert(target, type);
            var castPropertyValue = Expression.Convert(propertyValue, property.PropertyType);
            var getSetMethod = property.GetSetMethod();
            if (getSetMethod == null)
            {
                getSetMethod = property.GetSetMethod(true);
            }
            var call = Expression.Call(castTarget, getSetMethod, castPropertyValue);
            var lambda = Expression.Lambda<Action<object, object>>(call, target, propertyValue);
            return lambda.Compile();
        }
        public static Action<object, object> CreateSetPropertyValueAction(string typeName, string propertyName, bool isTypeFromAssembly = false)
        {
            Type type;
            if (isTypeFromAssembly)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().First
                                                                (
                                                                    (a) =>
                                                                    {
                                                                        return a.GetTypes().Any
                                                                                            (
                                                                                                (t) =>
                                                                                                {
                                                                                                    return (t.FullName == typeName);
                                                                                                }
                                                                                            );
                                                                    }
                                                                );
                type = assembly.GetType(typeName);
            }
            else
            {
                type = Type.GetType(typeName);
            }
            return CreateSetPropertyValueAction(type, propertyName);
        }
        public static Action<object, TProperty> CreateSetPropertyValueAction<TProperty>(Type type, string propertyName)
        {
            var property = type.GetProperty(propertyName);
            var target = Expression.Parameter(typeof(object));
            var propertyValue = Expression.Parameter(typeof(TProperty));
            var castTarget = Expression.Convert(target, type);
            var castPropertyValue = Expression.Convert(propertyValue, property.PropertyType);
            var getSetMethod = property.GetSetMethod();
            if (getSetMethod == null)
            {
                getSetMethod = property.GetSetMethod(true);
            }
            var call = Expression.Call(castTarget, getSetMethod, castPropertyValue);
            return Expression.Lambda<Action<object, TProperty>>(call, target, propertyValue).Compile();
        }
        public static Action<object, TProperty> CreateSetPropertyValueAction<TProperty>(string typeName, string propertyName, bool isTypeFromAssembly = false)
        {
            Type type;
            if (isTypeFromAssembly)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().First
                                                                (
                                                                    (a) =>
                                                                    {
                                                                        return a.GetTypes().Any
                                                                                            (
                                                                                                (t) =>
                                                                                                {
                                                                                                    return (t.FullName == typeName);
                                                                                                }
                                                                                            );
                                                                    }
                                                                );
                type = assembly.GetType(typeName);
            }
            else
            {
                type = Type.GetType(typeName);
            }
            return CreateSetPropertyValueAction<TProperty>(type, propertyName);
        }
        public static Action<object> CreateSetStaticPropertyValueAction(Type type, string propertyName)
        {
            var property = type.GetProperty(propertyName);
            var propertyValue = Expression.Parameter(typeof(object));
            var castPropertyValue = Expression.Convert(propertyValue, property.PropertyType);
            var getSetMethod = property.GetSetMethod();
            if (getSetMethod == null)
            {
                getSetMethod = property.GetSetMethod(true);
            }
            var call = Expression.Call(null, getSetMethod, castPropertyValue);
            var lambda = Expression.Lambda<Action<object>>(call, propertyValue);
            return lambda.Compile();
        }
        public static Action<object> CreateSetStaticPropertyValueAction(string typeName, string propertyName, bool isTypeFromAssembly = false)
        {
            Type type;
            if (isTypeFromAssembly)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().First
                                                                (
                                                                    (a) =>
                                                                    {
                                                                        return a.GetTypes().Any
                                                                                            (
                                                                                                (t) =>
                                                                                                {
                                                                                                    return (t.FullName == typeName);
                                                                                                }
                                                                                            );
                                                                    }
                                                                );
                type = assembly.GetType(typeName);
            }
            else
            {
                type = Type.GetType(typeName);
            }
            return CreateSetStaticPropertyValueAction(type, propertyName);
        }
        public static Action<TProperty> CreateSetStaticPropertyValueAction<TProperty>(Type type, string propertyName)
        {
            var property = type.GetProperty(propertyName);
            var propertyValue = Expression.Parameter(typeof(TProperty));
            //var castPropertyValue = Expression.Convert(propertyValue, property.PropertyType);
            var getSetMethod = property.GetSetMethod();
            if (getSetMethod == null)
            {
                getSetMethod = property.GetSetMethod(true);
            }
            var call = Expression.Call(null, getSetMethod, propertyValue);
            var lambda = Expression.Lambda<Action<TProperty>>(call, propertyValue);
            return lambda.Compile();
        }
        public static Action<TProperty> CreateSetStaticPropertyValueAction<TProperty>(string typeName, string propertyName, bool isTypeFromAssembly = false)
        {
            Type type;
            if (isTypeFromAssembly)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().First
                                                                (
                                                                    (a) =>
                                                                    {
                                                                        return a.GetTypes().Any
                                                                                            (
                                                                                                (t) =>
                                                                                                {
                                                                                                    return (t.FullName == typeName);
                                                                                                }
                                                                                            );
                                                                    }
                                                                );
                type = assembly.GetType(typeName);
            }
            else
            {

                type = Type.GetType(typeName);
            }
            return CreateSetStaticPropertyValueAction<TProperty>(type, propertyName);
        }
    }
}
