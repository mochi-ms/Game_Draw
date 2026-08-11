using GameDraw.Core.Profiles;
using Microsoft.UI.Xaml.Controls;

namespace GameDraw_App.Dialogs;

public sealed partial class ProfileEditorDialog : ContentDialog
{
    public ProfileEditorDialog(GameProfile? profile)
    {
        InitializeComponent();
        ProfileNameBox.Text = profile?.Name ?? string.Empty;
        GameNameBox.Text = profile?.GameName ?? string.Empty;
    }

    public string ProfileName => ProfileNameBox.Text.Trim();

    public string GameName => GameNameBox.Text.Trim();
}
