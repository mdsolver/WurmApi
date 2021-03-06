﻿using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace AldursLab.WurmApi.Utility
{
    /// <summary>
    /// Maintains a dedicated thread to run job delegate at. Job is executed repeatedly in response to signals. 
    /// If signals arrive when job is already running, another execution is queued immediately.
    /// </summary>
    sealed class RepeatableThreadedOperation : IDisposable
    {
        [CanBeNull]
        readonly IWurmApiEventMarshaller eventMarshaller;
        readonly Task task;
        volatile bool exit = false;
        readonly AutoResetEvent autoResetEvent = new AutoResetEvent(false);
        readonly TaskCompletionSource<bool> operationCompletedAtLeastOnceAwaiter = new TaskCompletionSource<bool>();

        /// <summary>
        /// </summary>
        /// <param name="job">
        /// Delegate to execute after receiving signals.
        /// </param>
        /// <param name="eventMarshaller">Optional thread marshaller of the events.</param>
        public RepeatableThreadedOperation([NotNull] Action job, IWurmApiEventMarshaller eventMarshaller = null)
        {
            this.eventMarshaller = eventMarshaller;

            if (job == null) throw new ArgumentNullException(nameof(job));
            task = new Task(() =>
            {
                while (true)
                {
                    autoResetEvent.WaitOne();
                    if (exit)
                    {
                        break;
                    }
                    try
                    {
                        job();
                        Task.Run(() => operationCompletedAtLeastOnceAwaiter.TrySetResult(true));
                    }
                    catch (Exception exception)
                    {
                        try
                        {
                            OnOperationFaulted(new ExceptionEventArgs(exception));
                        }
                        catch (Exception)
                        {
                            // nothing more to be done here
                        }
                    }
                    if (exit)
                    {
                        break;
                    }
                }
            }, TaskCreationOptions.LongRunning);
            task.Start();
        }

        /// <summary>
        /// Triggered if job delegate has thrown an unhandled exception.
        /// Should this handler throw unhandled exception and should it return to the underlying thread, it will be ignored.
        /// </summary>
        public event EventHandler<ExceptionEventArgs> OperationError;

        /// <summary>
        /// Indicates, if operation had been successfully executed at least once.
        /// </summary>
        public bool OperationCompletedAtLeastOnce => operationCompletedAtLeastOnceAwaiter.Task.IsCompleted;

        /// <summary>
        /// Task, that transitions to completion, when operation has been successfully executed for the first time.
        /// </summary>
        public Task OperationCompletedAtLeastOnceAwaiter => operationCompletedAtLeastOnceAwaiter.Task;

        /// <summary>
        /// Synchronously waits for when operation is successfully executed for the first time
        /// </summary>
        /// <param name="timeout">Null - wait indefinitely</param>
        /// <exception cref="TimeoutException"></exception>
        public void WaitSynchronouslyForInitialOperation(TimeSpan? timeout = null)
        {
            if (timeout != null)
            {
                if (OperationCompletedAtLeastOnceAwaiter.Wait(timeout.Value))
                {
                    throw new TimeoutException();
                }
            }
            OperationCompletedAtLeastOnceAwaiter.Wait();
        }

        /// <summary>
        /// Signals to the operation, that it should execute.
        /// If signalled while operation is running, it will be queued for another execution.
        /// </summary>
        public void Signal()
        {
            autoResetEvent.Set();
        }

        public void Dispose()
        {
            // stop the operation
            exit = true;
            autoResetEvent.Set();
            try
            {
                if (!task.Wait(50))
                {
                    autoResetEvent.Set();
                    if (task.Wait(10000))
                    {
                        task.Dispose();
                    }
                }
            }
            catch (AggregateException)
            {
                // task might be faulted, which is ultimately irrelevant for cleanup
            }
        }

        void OnOperationFaulted(ExceptionEventArgs e)
        {
            var handler = OperationError;
            if (handler != null)
            {
                if (eventMarshaller != null)
                {
                    eventMarshaller.Marshal(() =>
                    {
                        handler(this, e);
                    });
                }
                else
                {
                    handler(this, e);
                }
            }
        }
    }
}
