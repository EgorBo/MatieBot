
var cts = new CancellationTokenSource();
await new MatieBot().StartListeningAsync(cts);
Console.WriteLine("Listening... Press any key to stop the bot.");
Console.ReadKey();
cts.Cancel();