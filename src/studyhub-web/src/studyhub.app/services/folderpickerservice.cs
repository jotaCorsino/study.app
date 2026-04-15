using studyhub.application.Interfaces;

namespace studyhub.app.services;

public class FolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
#if WINDOWS
        var nativeWindow = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (nativeWindow == null)
        {
            throw new InvalidOperationException("A janela atual nao esta disponivel para abrir o seletor de pastas.");
        }

        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);

        var folder = await picker.PickSingleFolderAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return folder?.Path;
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("O seletor de pastas local nao esta disponivel nesta plataforma.");
#endif
    }
}
