using System.Text;

namespace testWait
{
    /// <summary>
    /// Class to record information on timing of tasks
    /// </summary>
    class Event
    {
        public int Id { get; set; }

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool CancelledBeforeStart { get; set; } = false;
        public DateTime? CancelTriggerTime { get; set; }
        public DateTime? CancelExceptionTime { get; set; }

        // Properties to make sense of the recorded times
        public double TotalDuration { get => (EndTime - StartTime).TotalSeconds; }
        public double TriggeredAfter {  get => CancelTriggerTime != null ? (CancelTriggerTime.Value - StartTime).TotalSeconds : -1.0; }
        public double ExceptionAfter { get => CancelExceptionTime != null ? (CancelExceptionTime.Value - StartTime).TotalSeconds : -1.0; }

        public override string ToString() => $"{Id},{StartTime},{TotalDuration},{TriggeredAfter},{ExceptionAfter},{CancelledBeforeStart}";
    }
    internal class Program
    {
        static async Task Main(string[] args)
        {
            //////////////////////////////////////////////////////
            // RUN CONFIGURATION
            // set induceIssue to true to observe excessive delay.  Set to false to observe expected behaviors
            bool induceIssue = true;
            int taskDelayMs = 20000;    // Task.DelayAsync for 20 seconds
            //////////////////////////////////////////////////////

            // Hold results from all calls.  Outer Main has Id=-1
            List<Event> Events = new List<Event>();

            int taskCount = Environment.ProcessorCount << 1;    // twice as many tasks as logical cores
            if (induceIssue) taskCount = Environment.ProcessorCount << 5; // 2^5 as may tasks as cores

            Console.WriteLine($"Starting {taskCount} tasks on {Environment.ProcessorCount} Logical Cores");
            DateTime start = DateTime.Now;
            DateTime end = start;
            var te = new Event
            {
                Id = -1,
                StartTime = start,
                EndTime = end
            };
            // Add the event representing the entire "Main"
            Events.Add(te);
            using var cts = new CancellationTokenSource();
            
            // record the time when we detected the token was triggered
            cts.Token.Register(() => end = DateTime.Now);

            // create an array of cancellable tasks
            var tasks = (from i in Enumerable.Range(0, taskCount) select DoSomething(i, taskDelayMs, cts.Token)).ToArray();
            // try with Task.Run to see if it makes a difference (it does now))
            //var tasks = (from i in Enumerable.Range(0, taskCount) select Task.Run(async () => await DoSomething(i, taskDelayMs, cts.Token), cts.Token)).ToArray();

            // CancelAfter is what we want, but let's call cancel, explicitly, to observe delays
            //cts.CancelAfter(200);

            // wait 100ms to trigger the cancellation so that we have a chance to enter into the Task.Delay(...) calls
            var triggerTask = Task.Run(async () =>
            {
                await Task.Delay(100);
                DateTime cancelStart = DateTime.Now;
                var triggerTime = (cancelStart - start).TotalSeconds;
                Console.WriteLine($"Cancelling work after {triggerTime}s");
                cts.Cancel();
                DateTime cancelEnd = DateTime.Now;
                triggerTime = (cancelEnd - start).TotalSeconds;
                var cancelDuration = (cancelEnd - cancelStart).TotalSeconds;
                // report time at which the token source finished the call to Cancel() (observe long delay)
                Console.WriteLine($"After calling cancel: {triggerTime}s (ctr..Cancel() duration: {cancelDuration})");
                te.CancelTriggerTime = DateTime.Now;
            });

            try
            {
                // use wait instead of when to pass the token into the WaitAll rather than relying on "DoSomething"
                //Task.WaitAll(tasks, cts.Token);
                Events.AddRange(await Task.WhenAll(tasks));
            }
            catch (OperationCanceledException oce)
            {
                // records the time when the exception threw the OperationCancelledException (if it is thrown)
                te.CancelExceptionTime = DateTime.Now;
                Console.WriteLine("Main Task Cancelled Exception");
            }

            te.EndTime = DateTime.Now;

            var duration = (DateTime.Now - start).TotalSeconds;
            var cancelAfter = (end - start).TotalSeconds;

            await triggerTask;
            //wait for them all to _actually_ finish
            //Events.AddRange(await Task.WhenAll(tasks));

            #region Build results String

            var sb = new StringBuilder();
            // sort the events by when the cancellation token was triggered
            foreach (var e in Events.OrderBy(e => e.TriggeredAfter).ToList())
            {
                sb.AppendLine(e.ToString());
            }

            #endregion

            // Write out all of the results
            Console.Write(sb.ToString());

            Console.WriteLine($"MainTask, taskDuration: {duration}, cancelAfter: {cancelAfter}");
            Console.WriteLine("Done processing. Press any key");
            Console.ReadKey();
        }
        static async Task<Event> DoSomething(int i, int delayMs, CancellationToken token)
        {
            Event e = new();
            //lock (Events) Events.Add(e);
            try
            {
                e.Id = i;
                //lock(log) log.AppendLine($"{i} started");
                e.StartTime = DateTime.Now;
                e.EndTime = e.StartTime;

                // record the time when we detected the token was triggered
                token.Register(() => e.CancelTriggerTime = DateTime.Now);

                if (token.IsCancellationRequested)
                {
                    e.CancelledBeforeStart = true;
                    return e;
                }
                try
                {
                    await Task.Delay(delayMs, token);
                }
                catch (TaskCanceledException tce)
                {
                    e.CancelExceptionTime = DateTime.Now;
                }
                e.EndTime = DateTime.Now;
                //await Task.Delay(20);
                return e;
            }
            finally
            {
                e.EndTime = DateTime.Now;
            }
        }
    }
}