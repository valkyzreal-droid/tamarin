using System.Globalization;

namespace Tamarin.App;

/// <summary>
/// All user-facing text, Portuguese or English by the Windows display language.
/// Used from XAML with {x:Static local:Strings.Name} and from code directly.
/// </summary>
internal static class Strings
{
    private static readonly bool Pt =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pt", System.StringComparison.OrdinalIgnoreCase);

    private static string T(string pt, string en) => Pt ? pt : en;

    public static string AppName => "Tamarin";

    // Capture window
    public static string EditorHint => T("Comece a digitar…", "Start typing…");
    public static string TipHide => T("esconde a janela", "hides the window");
    public static string TipSearch => T("busca nas notas", "searches your notes");
    public static string TipNew => T("começa outra nota", "starts a new note");
    public static string EditedAt(string when) => T($"editada {when}", $"edited {when}");
    public static string Resize => T("Redimensionar", "Resize");
    public static string Pin => T("Manter sobre outras janelas", "Keep on top of other windows");
    public static string Menu => T("Menu", "Menu");
    public static string NewNote => T("Nova nota", "New note");
    public static string Search => T("Buscar notas", "Search notes");
    public static string SearchPlaceholder => T("Buscar…", "Search…");
    public static string NoResults => T("Nenhuma nota com isso. Tente outra palavra.", "No note has that. Try another word.");
    public static string Older => T("Nota anterior", "Older note");
    public static string Newer => T("Próxima nota", "Newer note");
    public static string Saved => T("Salvo", "Saved");
    public static string Saving => T("Salvando…", "Saving…");
    public static string SaveFailed => T("Não salvou. Confira se o disco ou a pasta permite gravar e tente de novo.", "Not saved. Check that the disk or folder allows writing, then retry.");
    public static string Retry => T("Tentar de novo", "Retry");
    public static string Words(int n) => n == 1 ? T("1 palavra", "1 word") : T($"{n} palavras", $"{n} words");
    public static string Chars(int n) => T($"{n} caracteres", $"{n} characters");
    public static string Copied => T("Copiado", "Copied");
    public static string MovedToTrash => T("Movida para a lixeira", "Moved to trash");
    public static string Undo => T("Desfazer", "Undo");
    public static string Position(int i, int n) => $"{i} / {n}";

    // Menu
    public static string MenuSettings => T("Configurações", "Settings");
    public static string MenuTrash => T("Lixeira", "Trash");
    public static string MenuCopy => T("Copiar nota", "Copy note");
    public static string MenuExportThis => T("Exportar esta nota…", "Export this note…");
    public static string MenuDelete => T("Apagar nota", "Delete note");
    public static string MenuQuit => T("Sair do Tamarin", "Quit Tamarin");

    // Tray
    public static string TrayOpen => T("Abrir", "Open");
    public static string TrayTooltip => T("Tamarin", "Tamarin");

    // Settings
    public static string SettingsTitle => T("Configurações", "Settings");
    public static string General => T("Geral", "General");
    public static string Hotkey => T("Atalho global", "Global hotkey");
    public static string HotkeyHelp => T("Abre o Tamarin de qualquer programa. Use Ctrl, Shift, Alt ou Win (Ctrl+Alt sozinhos não são aceitos).",
                                        "Opens Tamarin from any program. Use Ctrl, Shift, Alt or Win (Ctrl+Alt alone is not accepted).");
    public static string HotkeyRecord => T("Clique e pressione o atalho", "Click and press the shortcut");
    public static string HotkeyRecording => T("Pressione o atalho… (Esc cancela)", "Press the shortcut… (Esc cancels)");
    public static string HotkeyReset => T("Restaurar padrão", "Reset to default");
    public static string HotkeyInUse => T("Esse atalho já está em uso por outro programa. Escolha outro.", "That shortcut is already used by another program. Pick another.");
    public static string HotkeyNeedsModifier => T("Inclua Ctrl, Alt ou Win.", "Include Ctrl, Alt or Win.");
    public static string HotkeyAltGr => T("Ctrl+Alt sozinhos digitam caracteres em alguns teclados. Adicione Shift ou use Win.", "Ctrl+Alt alone types characters on some keyboards. Add Shift or use Win.");
    public static string HotkeyTooCommon => T("Esse atalho já é usado por todos os programas (copiar, colar, recortar…). Adicione Shift ou use Win.", "Every program already uses that shortcut (copy, paste, cut…). Add Shift or use Win.");
    public static string HotkeyUnknown => T("Tecla não reconhecida.", "Key not recognised.");
    public static string StartWithWindows => T("Iniciar com o Windows", "Start with Windows");
    public static string Theme => T("Tema", "Theme");
    public static string ThemeSystem => T("Sistema", "System");
    public static string ThemeLight => T("Claro", "Light");
    public static string ThemeDark => T("Escuro", "Dark");
    public static string TextSize => T("Tamanho do texto", "Text size");
    public static string Data => T("Dados", "Data");
    public static string DataFolder => T("Pasta dos dados", "Data folder");
    public static string OpenFolder => T("Abrir pasta", "Open folder");
    public static string Backup => T("Backup diário automático", "Automatic daily backup");
    public static string BackupHelp => T("Guarda cópias das últimas versões ao lado das notas.", "Keeps copies of recent versions next to your notes.");
    public static string BackupNow => T("Fazer backup agora", "Back up now");
    public static string BackupDone(string name) => T($"Backup criado: {name}", $"Backup created: {name}");
    public static string TrashRetention => T("Esvaziar a lixeira após", "Empty trash after");
    public static string Days(int n) => T($"{n} dias", $"{n} days");
    public static string ExportAll => T("Exportar todas as notas…", "Export all notes…");
    public static string ExportDone(int n) => T($"{n} notas exportadas", $"{n} notes exported");
    public static string Import => T("Importar arquivos .md / .txt…", "Import .md / .txt files…");
    public static string ImportDone(int n, int dup) => dup > 0
        ? T($"{n} importadas, {dup} já existiam", $"{n} imported, {dup} already there")
        : T($"{n} importadas", $"{n} imported");
    public static string Privacy => T("Privacidade", "Privacy");
    public static string PrivacyText => T("Suas notas ficam só neste computador. O Tamarin não envia nada pela internet e não tem conta nem telemetria.",
                                          "Your notes stay on this computer. Tamarin sends nothing over the internet and has no account or telemetry.");
    public static string Close => T("Fechar", "Close");

    // Trash
    public static string TrashTitle => T("Lixeira", "Trash");
    public static string TrashEmpty => T("A lixeira está vazia", "Trash is empty");
    public static string Restore => T("Restaurar", "Restore");
    public static string DeleteForever => T("Apagar de vez", "Delete forever");
    public static string EmptyTrash => T("Esvaziar lixeira", "Empty trash");
    public static string ConfirmEmptyTrash => T("Apagar todas as notas da lixeira para sempre? Isso não pode ser desfeito.", "Permanently delete every note in the trash? This cannot be undone.");
    public static string ConfirmDeleteForever => T("Apagar esta nota para sempre?", "Permanently delete this note?");
    public static string Cancel => T("Cancelar", "Cancel");
    public static string Delete => T("Apagar", "Delete");
    public static string EmptyNote => T("(nota vazia)", "(empty note)");

    // First run
    public static string WelcomeTitle => T("Bem-vindo ao Tamarin", "Welcome to Tamarin");
    public static string WelcomeBody(string hotkey) => T(
        $"Pressione {hotkey} em qualquer programa, escreva e feche. A nota é salva sozinha.",
        $"Press {hotkey} in any program, type, and close. The note saves itself.");
    public static string WelcomeTray => T("O Tamarin fica na área de notificação, perto do relógio.", "Tamarin lives in the notification area, near the clock.");
    public static string WelcomeStart => T("Começar", "Get started");
    public static string WelcomeChange => T("Mudar atalho", "Change shortcut");

    // Errors
    public static string AlreadyRunning => T("O Tamarin já está em execução.", "Tamarin is already running.");
    public static string DbTooNew => T("As notas foram criadas por uma versão mais nova do Tamarin. Atualize o aplicativo para abri-las.", "These notes were created by a newer version of Tamarin. Update the app to open them.");
    public static string DbRecovered(string name) => T($"O arquivo de notas estava danificado. Restauramos o backup {name}. O arquivo antigo foi guardado ao lado.", $"The notes file was damaged. We restored backup {name}. The old file was kept next to it.");
    public static string DbUnrecoverable => T("O arquivo de notas está danificado e não há backup. O arquivo foi guardado e um novo será criado.", "The notes file is damaged and there is no backup. It was kept aside and a new one will be created.");
    public static string StartupFailed => T("Não foi possível iniciar o Tamarin.", "Tamarin could not start.");
    public static string HotkeyFailedTitle => T("Atalho indisponível", "Shortcut unavailable");
}
