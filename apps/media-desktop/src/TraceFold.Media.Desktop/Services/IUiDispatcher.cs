namespace TraceFold.Media.Desktop.Services;

public interface IUiDispatcher
{
    void Enqueue(Action callback);
}
