# TestWait
Project to demonstrate long delays during CancellationTokenSource.Cancel() calls


## Notes
At the top of ```main```, set ```induceCancel = true;``` to increase the number of awaited tasks and demonstrate that something is keeping the tasks from respecting the triggered cancellation token in a timely fashion