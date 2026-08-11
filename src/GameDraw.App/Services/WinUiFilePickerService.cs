using Windows.Storage.Pickers;

namespace GameDraw_App.Services;

public sealed class WinUiFilePickerService
{
    public async Task<string?> PickImageAsync(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            throw new InvalidOperationException("The application window is not ready.");
        }

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".webp");
        picker.FileTypeFilter.Add(".bmp");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
