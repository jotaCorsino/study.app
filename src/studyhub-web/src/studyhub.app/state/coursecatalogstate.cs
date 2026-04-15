namespace studyhub.app.state;

public class CourseCatalogState
{
    public event Action? Changed;

    public void NotifyChanged()
    {
        Changed?.Invoke();
    }
}
