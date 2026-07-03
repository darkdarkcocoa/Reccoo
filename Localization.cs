using System.Globalization;
using System.Windows;

namespace Reccoo;

public enum AppLanguage { Korean, English }

/// <summary>
/// 초경량 로컬라이제이션. XAML은 {DynamicResource L_키}, 코드는 L10n.T("키")로 읽는다.
/// 언어 전환 시 Application.Resources의 문자열을 통째로 갈아끼우는 방식 —
/// ApplyTheme의 Recolor와 같은 패턴이라 별도 프레임워크가 필요 없다.
/// </summary>
public static class L10n
{
    public static AppLanguage Current { get; private set; } = AppLanguage.Korean;
    public static bool IsKorean => Current == AppLanguage.Korean;
    public static event Action? LanguageChanged;

    /// <summary>시스템 UI 언어에 맞춰 시작 언어 결정 (ko → 한국어, 그 외 → English).</summary>
    public static void Init()
    {
        Current = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko"
            ? AppLanguage.Korean
            : AppLanguage.English;
        Apply();
    }

    public static void Set(AppLanguage lang)
    {
        if (Current == lang) return;
        Current = lang;
        Apply();
        LanguageChanged?.Invoke();
    }

    public static string T(string key) => IsKorean ? Table[key].Ko : Table[key].En;

    public static string FormatMetaDate(DateTime dt) =>
        IsKorean ? dt.ToString("M월 d일 HH:mm")
                 : dt.ToString("MMM d, HH:mm", CultureInfo.InvariantCulture);

    public static string[] StartMessages => IsKorean ? StartKo : StartEn;
    public static string[] StopMessages  => IsKorean ? StopKo  : StopEn;
    public static string[] IdleMessages  => IsKorean ? IdleKo  : IdleEn;

    private static void Apply()
    {
        foreach (var (key, text) in Table)
            Application.Current.Resources["L_" + key] = IsKorean ? text.Ko : text.En;
    }

    private static readonly Dictionary<string, (string Ko, string En)> Table = new()
    {
        // 타이틀바
        ["WindowTitle"]     = ("♪  Reccoo — 귀여운 사운드 레코더  ♪", "♪  Reccoo — cute sound recorder  ♪"),
        ["ThemeTooltip"]    = ("테마 전환", "Toggle theme"),

        // 상태 표시
        ["StatusIdle"]      = ("○ 대기 중", "○ Standing by"),
        ["StatusRecording"] = ("● 녹음 중...", "● Recording..."),
        ["StatusPaused"]    = ("‖ 일시정지", "‖ Paused"),
        ["StatusCountdown"] = ("♪ 곧 시작...", "♪ Starting soon..."),
        ["SystemSound"]     = ("시스템 사운드", "system audio"),

        // 트랜스포트
        ["RecordStart"]     = ("녹음 시작", "Record"),
        ["Recording"]       = ("녹음 중...", "Recording..."),
        ["PauseBtn"]        = ("일시정지", "Pause"),
        ["ResumeBtn"]       = ("재개", "Resume"),
        ["StopBtn"]         = ("정지", "Stop"),
        ["ShortcutHint"]    = ("Space  녹음 토글   ·   P  일시정지   ·   Ctrl+O  폴더",
                               "Space  record   ·   P  pause   ·   Ctrl+O  folder"),

        // 설정 패널
        ["SettingsTitle"]   = ("⚙  녹음 설정", "⚙  Recording settings"),
        ["InputDevice"]     = ("🎤  입력 장치", "🎤  Input device"),
        ["OutputFormat"]    = ("🔊  출력 포맷", "🔊  Output format"),
        ["Mp3Quality"]      = ("↳  MP3 품질", "↳  MP3 quality"),
        ["InputLevel"]      = ("입력 레벨", "Input level"),
        ["SaveFolder"]      = ("저장 폴더", "Save folder"),
        ["ChangeBtn"]       = ("변경", "Change"),

        // 마스코트 카드
        ["MascotName"]      = ("레코 (Recco)", "Recco (레코)"),
        ["Hello"]           = ("안녕! 오늘은\n뭘 녹음할까?", "Hi! What are we\nrecording today?"),

        // 보관함
        ["LibraryTitle"]    = ("내 녹음 보관함", "My recordings"),
        ["CountFmt"]        = (" · {0}개", " · {0}"),
        ["CardTooltip"]     = ("더블클릭으로 이름 변경 · 끌어서 내보내기", "Double-click to rename · drag out to export"),
        ["EmptyTitle"]      = ("아직 녹음이 없어요.", "No recordings yet."),
        ["EmptyHint"]       = ("Space를 눌러 시작!", "Press Space to start!"),

        // 다이얼로그 / 카운트다운
        ["FolderDialogTitle"] = ("저장 폴더 선택", "Choose a save folder"),
        ["Count3"]          = ("셋!", "Three!"),
        ["Count2"]          = ("둘!", "Two!"),
        ["Count1"]          = ("하나!", "One!"),

        // 마스코트 안내/오류 대사
        ["MsgClosing"]      = ("마무리 중...\n잠깐만!", "Wrapping up...\njust a sec!"),
        ["MsgSaving"]       = ("저장 중...\n잠깐만!", "Saving...\njust a sec!"),
        ["MsgCancelled"]    = ("취소했어!\n다시 누르면 시작~", "Cancelled!\nPress again to go~"),
        ["MsgPaused"]       = ("잠깐 멈췄어!\n준비되면 재개~", "Taking a breather!\nResume when ready~"),
        ["MsgResumed"]      = ("♪ 다시 들어볼게!", "♪ Back to listening!"),
        ["MsgPickDevice"]   = ("장치를 먼저\n선택해줘!", "Pick a device\nfirst!"),
        ["MsgPlaying"]      = ("재생 중!\n♪~", "Now playing!\n♪~"),
        ["MsgNameExists"]   = ("같은 이름이\n이미 있어ㅠ", "That name is\nalready taken :("),
        ["MsgDeviceLoadFail"] = ("장치 로드 실패ㅠ", "Couldn't load devices :("),
        ["MsgFolderOpenFail"] = ("폴더 열기 실패ㅠ", "Couldn't open folder :("),
        ["MsgStartFail"]    = ("시작 실패ㅠ", "Couldn't start :("),
        ["MsgError"]        = ("오류ㅠ", "Oops, error :("),
        ["MsgRenameFail"]   = ("이름 바꾸기 실패", "Rename failed"),
        ["MsgPlayFail"]     = ("재생 실패ㅠ", "Playback failed :("),
        ["MsgDeleteFail"]   = ("삭제 실패ㅠ", "Delete failed :("),
    };

    // ===== 마스코트 대사 풀 — 언어별로 같은 결의 목소리를 유지한다 =====

    private static readonly string[] StartKo =
    {
        "♪ 잘 들리고 있어!\n좋은 소리야~",
        "오 이거 진짜\n좋은데?!",
        "녹음 시작!\n맘에 들 거야 ♡",
        "쉿... 다 듣고\n있어~",
        "어디 누가 떠드는\n거야 ㅋㅋ",
        "♪ 흥얼흥얼...",
        "이거 명곡각!\n잘 잡자",
        "딱 좋은 타이밍!",
        "음원 수집 중...",
        "오케이 OK!\n잘 들어가는 중",
    };

    private static readonly string[] StartEn =
    {
        "♪ Hearing it loud\nand clear~",
        "Ooh, this one's\nactually good?!",
        "Recording!\nYou'll love this ♡",
        "Shh... I'm getting\nall of it~",
        "Who's making all\nthat noise? hehe",
        "♪ humming along...",
        "Banger alert!\nLet's catch it",
        "Perfect timing!",
        "Collecting sounds...",
        "Okay OK!\nComing in nicely",
    };

    private static readonly string[] StopKo =
    {
        "잘 저장됐어!\n또 녹음할까?",
        "굿굿! 들어보자~",
        "이거 보관해두자!\n♡",
        "오 좋은 소리\n잡았어 ♪",
        "완벽! 명작!",
        "또 듣고 싶으면\n클릭해줘",
        "녹음 끝!\n잘 했어 ♡",
        "와 길게 했네!\n수고했어~",
        "이것도 추가!\n보관함이 풍성~",
        "딱 좋은 길이!",
    };

    private static readonly string[] StopEn =
    {
        "Saved!\nRecord another?",
        "Nice! Let's\nhear it~",
        "This one's a\nkeeper! ♡",
        "Ooh, caught a\ngood one ♪",
        "Perfect!\nA masterpiece!",
        "Click me if you\nwant a replay",
        "All done!\nGreat job ♡",
        "Wow, a long one!\nNice work~",
        "Added!\nThe shelf grows~",
        "Just the right\nlength!",
    };

    private static readonly string[] IdleKo =
    {
        "안녕! 오늘은\n뭘 녹음할까?",
        "또 만났네 ♡",
        "Space 누르면\n시작!",
        "♪ 음악 듣자~",
        "오늘 컨디션\n좋다!",
        "뭐 재미난 거\n있어?",
    };

    private static readonly string[] IdleEn =
    {
        "Hi! What are we\nrecording today?",
        "We meet again ♡",
        "Press Space\nto start!",
        "♪ Let's listen to\nsome music~",
        "Feeling great\ntoday!",
        "Got anything\nfun?",
    };
}
