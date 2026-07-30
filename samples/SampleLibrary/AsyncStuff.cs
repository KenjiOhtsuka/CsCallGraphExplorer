namespace SampleLibrary;

public class AsyncStuff
{
    public async Task<int> ComputeAsync(int input)
    {
        await Task.Delay(1);
        return input * 2;
    }

    public async Task<string> FetchAsync(string url)
    {
        await Task.Delay(1);
        return $"data from {url}";
    }

    public async void FireAndForget()
    {
        await Task.Delay(1);
    }

    public async ValueTask<int> GetValueAsync()
    {
        await Task.Delay(1);
        return 42;
    }
}
