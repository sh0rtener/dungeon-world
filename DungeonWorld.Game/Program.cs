using DungeonWorld.Game;
using DungeonWorld.Game.Map;

var player = new Player();
var field = new Field(player);
var gameCycle = new GameCycle(player, field);

var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += Console_CancelKeyPress;

try
{
    await gameCycle.RunAsync(cancellationTokenSource.Token);
} 
finally
{
    Console.CancelKeyPress -= Console_CancelKeyPress;
}
void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
{
    cancellationTokenSource.Cancel();
}