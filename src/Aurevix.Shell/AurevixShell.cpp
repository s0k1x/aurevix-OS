// AUREVIX Shell — componente nativo que da el estilo AUREVIX a la barra de tareas y al menú Inicio.
//
// Cómo funciona: AUREVIX Control Center llama a InitializeXamlDiagnosticsEx (API pública de Windows, la misma que
// usa Visual Studio para inspeccionar interfaces XAML). Windows carga esta DLL dentro del Explorador o del menú
// Inicio y le entrega el árbol visual. Aquí solo se cambian propiedades de aspecto (colores, bordes, esquinas) de
// elementos concretos. No se modifica ningún archivo de Windows: al reiniciar el Explorador sin AUREVIX Shell,
// todo vuelve a ser Windows 11 original.
//
// Reglas de seguridad del código: nada de excepciones fuera de las llamadas COM (todo va en try/catch), solo se
// tocan elementos con nombre conocido, y cada DLL se activa una sola vez por proceso.

#include <unknwn.h>
#include <windows.h>
#include <ocidl.h>
#include <inspectable.h>
#include <xamlOM.h>
#include <string>
#include <chrono>
#include <limits>
#include <atomic>
#include <mutex>
#include <vector>
#include <algorithm>
#include <cstdio>
#include <share.h>

// windows.h define GetCurrentTime como macro y choca con Storyboard::GetCurrentTime de C++/WinRT.
#ifdef GetCurrentTime
#undef GetCurrentTime
#endif
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Input.h>
#include <winrt/Windows.UI.Xaml.Media.Animation.h>
#include <winrt/Windows.UI.Xaml.Media.Imaging.h>
#include <winrt/Windows.Storage.Streams.h>
#include <shlwapi.h>
#include <shcore.h>
#include <dwmapi.h>

namespace wux = winrt::Windows::UI::Xaml;
namespace wuxm = winrt::Windows::UI::Xaml::Media;
namespace wuxs = winrt::Windows::UI::Xaml::Shapes;
namespace wuxc = winrt::Windows::UI::Xaml::Controls;
namespace wuxa = winrt::Windows::UI::Xaml::Media::Animation;
using winrt::Windows::UI::Color;

// {8C2D5B1E-6A3F-4E8B-9D47-A7E15C0B3F21}
static constexpr GUID CLSID_AurevixTap = { 0x8c2d5b1e, 0x6a3f, 0x4e8b, { 0x9d, 0x47, 0xa7, 0xe1, 0x5c, 0x0b, 0x3f, 0x21 } };

// Paleta AUREVIX: clara (predeterminada) u oscura, según el modo de Windows.
static Color Graphite{ 0xA6, 0x0B, 0x0B, 0x0E };   // fondo de la barra: grafito translúcido (efecto cristal)
static Color Sheen{ 0x26, 0xFF, 0xFF, 0xFF };      // brillo superior del cristal
static Color Lime{ 0xFF, 0x8D, 0xE0, 0x3C };       // acento
static Color Page{ 0xB8, 0x0B, 0x0B, 0x0E };       // fondo de Configuración (deja ver el Mica de Windows)

// La paleta se recarga en vivo desde otro hilo: toda lectura/escritura pasa por este mutex.
static std::mutex g_paletteMutex;
static std::atomic<bool> g_lightTheme{ true };
static Color CurLime() { std::lock_guard<std::mutex> l(g_paletteMutex); return Lime; }
static Color CurPage() { std::lock_guard<std::mutex> l(g_paletteMutex); return Page; }
static Color CurGraphite() { std::lock_guard<std::mutex> l(g_paletteMutex); return Graphite; }
static Color CurSheen() { std::lock_guard<std::mutex> l(g_paletteMutex); return Sheen; }

static void LoadPalette()
{
    DWORD light = 1, size = sizeof(light);
    RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", L"SystemUsesLightTheme",
                 RRF_RT_REG_DWORD, nullptr, &light, &size);
    // Color de acento de Windows: DWORD 0xAABBGGRR. Si no existe, se queda el verde AUREVIX.
    DWORD accent = 0, asize = sizeof(accent);
    bool hasAccent = RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\DWM", L"AccentColor",
                                  RRF_RT_REG_DWORD, nullptr, &accent, &asize) == ERROR_SUCCESS;
    std::lock_guard<std::mutex> l(g_paletteMutex);
    g_lightTheme = light != 0;
    if (light)
    {
        Graphite = { 0x9E, 0xF1, 0xF6, 0xEA };   // blanco translúcido con un toque verde AUREVIX
        Sheen = { 0x8C, 0xFF, 0xFF, 0xFF };
        Lime = { 0xFF, 0x4E, 0x9A, 0x1E };
        Page = { 0xB0, 0xF1, 0xF6, 0xEC };
    }
    else
    {
        Graphite = { 0xA6, 0x0B, 0x0B, 0x0E };
        Sheen = { 0x26, 0xFF, 0xFF, 0xFF };
        Lime = { 0xFF, 0x8D, 0xE0, 0x3C };
        Page = { 0xB8, 0x0B, 0x0B, 0x0E };
    }
    if (hasAccent)
    {
        double r = accent & 0xFF, g = (accent >> 8) & 0xFF, b = (accent >> 16) & 0xFF;
        // En tema claro, un acento muy claro se oscurece un 35 % para que contraste.
        double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
        if (light && lum > 0.6) { r *= 0.65; g *= 0.65; b *= 0.65; }
        // En tema oscuro, un acento muy oscuro se mezcla un 40 % con blanco para que se lea.
        else if (!light && lum < 0.25) { r += (255.0 - r) * 0.4; g += (255.0 - g) * 0.4; b += (255.0 - b) * 0.4; }
        Lime = { 0xFF, static_cast<uint8_t>(r), static_cast<uint8_t>(g), static_cast<uint8_t>(b) };
    }
}

static std::atomic<bool> g_active{ false };
static DWORD WINAPI GlassExplorerWindows(LPVOID);
static std::atomic<long> g_styled{ 0 };

// ------------------------------------------------------------------ registro (diagnóstico)

static void Log(const wchar_t* fmt, ...)
{
    wchar_t msg[512];
    va_list args;
    va_start(args, fmt);
    _vsnwprintf_s(msg, _TRUNCATE, fmt, args);
    va_end(args);
    OutputDebugStringW(msg);
    wchar_t path[MAX_PATH];
    if (!GetTempPathW(MAX_PATH, path)) return;
    wcscat_s(path, L"aurevix-shell.log");
    FILE* f = nullptr;
    if (_wfopen_s(&f, path, L"a, ccs=UTF-8") == 0 && f)
    {
        SYSTEMTIME t; GetLocalTime(&t);
        fwprintf(f, L"%02d:%02d:%02d [%u] %s\n", t.wHour, t.wMinute, t.wSecond, GetCurrentProcessId(), msg);
        fclose(f);
    }
}

// Diagnóstico (solo máquina de pruebas): si junto a la DLL existe «dump.flag», se guarda la estructura de la interfaz.
static HMODULE g_module = nullptr;
static FILE* s_dump = nullptr;
static long s_dumpLines = 0;
static void OpenDump(std::wstring const& process)
{
    wchar_t dll[MAX_PATH]{};
    GetModuleFileNameW(g_module, dll, MAX_PATH);
    std::wstring flag(dll);
    flag = flag.substr(0, flag.find_last_of(L"\\/") + 1) + L"dump.flag";
    if (GetFileAttributesW(flag.c_str()) == INVALID_FILE_ATTRIBUTES) return;
    std::wstring dir = flag.substr(0, flag.find_last_of(L"\\/") + 1);
    s_dump = _wfsopen((dir + L"aurevix-tree-" + process + L".log").c_str(), L"w, ccs=UTF-8", _SH_DENYNO); // legible mientras está abierto
    if (s_dump) return;
    wchar_t path[MAX_PATH];
    if (!GetTempPathW(MAX_PATH, path)) return;
    std::wstring out = std::wstring(path) + L"aurevix-tree-" + process + L".log";
    s_dump = _wfsopen(out.c_str(), L"w, ccs=UTF-8", _SH_DENYNO);
}
static void DumpElement(unsigned long long parent, unsigned long long handle, LPCWSTR type, LPCWSTR name)
{
    if (!s_dump || s_dumpLines > 20000) return;
    fwprintf(s_dump, L"%llx > %llx  %s  #%s\n", parent, handle, type ? type : L"?", name ? name : L"");
    if (++s_dumpLines % 200 == 0) fflush(s_dump);
}

static std::wstring ProcessName()
{
    wchar_t path[MAX_PATH]{};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    std::wstring p(path);
    auto slash = p.find_last_of(L"\\/");
    std::wstring name = slash == std::wstring::npos ? p : p.substr(slash + 1);
    for (auto& c : name) c = towlower(c);
    return name;
}

// ------------------------------------------------------------------ estilo

static bool HasAncestor(wux::DependencyObject el, std::wstring_view className, int maxDepth = 8)
{
    for (int i = 0; i < maxDepth && el; i++)
    {
        el = wuxm::VisualTreeHelper::GetParent(el);
        if (el && winrt::get_class_name(el) == className) return true;
    }
    return false;
}

static bool SameColor(wuxm::Brush const& b, Color c)
{
    auto s = b.try_as<wuxm::SolidColorBrush>();
    if (!s) return false;
    auto x = s.Color();
    return x.A == c.A && x.R == c.R && x.G == c.G && x.B == c.B;
}

static wuxm::Brush GlassBrush()
{
    wuxm::LinearGradientBrush g;
    g.StartPoint({ 0, 0 }); g.EndPoint({ 0, 1 });
    Color sheen = CurSheen(), graphite = CurGraphite();
    wuxm::GradientStop top; top.Color(sheen); top.Offset(0);
    wuxm::GradientStop mid; mid.Color(graphite); mid.Offset(0.45);
    wuxm::GradientStop bottom; bottom.Color(graphite); bottom.Offset(1);
    g.GradientStops().Append(top); g.GradientStops().Append(mid); g.GradientStops().Append(bottom);
    return g;
}

// ------------------------------------------------------------------ elementos estilizados (para recargar la paleta en vivo)

enum class Role { GlassFill, AppIndicator, StartOverlay, SettingsPage, SettingsSelection, StartBorder };

struct StyledRef
{
    unsigned long long id;
    Role role;
    winrt::weak_ref<wux::DependencyObject> ref;
    winrt::Windows::UI::Core::CoreDispatcher dispatcher{ nullptr };   // agile: se puede usar desde cualquier hilo
};
static std::mutex g_styledMutex;
static std::vector<StyledRef> g_styledList;

static void Register(Role role, wux::DependencyObject const& obj)
{
    try
    {
        static unsigned long long nextId = 0;
        auto w = winrt::make_weak(obj);
        auto d = obj.Dispatcher();   // Register se llama en el hilo de UI del elemento
        if (!d) return;              // islas XAML sin dispatcher: no se puede recargar en vivo, no se registra
        std::lock_guard<std::mutex> l(g_styledMutex);
        // Se quitan las entradas cuyo elemento ya no existe y se evitan duplicados (mismo objeto y mismo rol).
        bool duplicate = false;
        // Solo se resuelven los elementos de este mismo hilo de UI (los de otros hilos no se tocan aquí).
        for (auto it = g_styledList.begin(); it != g_styledList.end();)
        {
            if (it->dispatcher != d) { ++it; continue; }
            auto other = it->ref.get();
            if (!other) { it = g_styledList.erase(it); continue; }
            if (it->role == role && other == obj) duplicate = true;
            ++it;
        }
        if (duplicate) return;
        g_styledList.push_back({ ++nextId, role, w, d });
    }
    catch (...) {}
}

// Color actual (de la paleta global) de los roles que usan un color liso.
static Color ColorFor(Role role)
{
    return role == Role::SettingsPage ? CurPage() : CurLime();
}

static bool EqColor(Color a, Color b) { return a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B; }

// ¿El pincel es el cristal con la paleta actual?
static bool IsCurrentGlass(wuxm::Brush const& b)
{
    auto g = b.try_as<wuxm::LinearGradientBrush>();
    if (!g) return false;
    auto stops = g.GradientStops();
    if (stops.Size() != 3) return false;
    return EqColor(stops.GetAt(0).Color(), CurSheen()) && EqColor(stops.GetAt(1).Color(), CurGraphite());
}

static bool SetPageBackground(wux::UIElement const& content)
{
    auto brush = wuxm::SolidColorBrush(CurPage());
    if (auto panel = content.try_as<wuxc::Panel>()) { panel.Background(brush); return true; }
    if (auto control = content.try_as<wuxc::Control>()) { control.Background(brush); return true; }
    if (auto border = content.try_as<wuxc::Border>()) { border.Background(brush); return true; }
    return false;
}

// Vuelve a aplicar el estilo a un elemento con la paleta actual (se ejecuta en el hilo de UI del elemento).
static void ReapplyOne(Role role, wux::DependencyObject const& obj)
{
    switch (role)
    {
    case Role::GlassFill:
        if (auto sh = obj.try_as<wuxs::Shape>()) sh.Fill(GlassBrush());
        break;
    case Role::AppIndicator:
    case Role::SettingsSelection:
        if (auto sh = obj.try_as<wuxs::Shape>()) sh.Fill(wuxm::SolidColorBrush(CurLime()));
        break;
    case Role::StartOverlay:
        if (auto b = obj.try_as<wuxc::Border>()) b.Background(GlassBrush());
        break;
    case Role::SettingsPage:
        if (auto ui = obj.try_as<wux::UIElement>()) SetPageBackground(ui);
        break;
    case Role::StartBorder:
        if (auto b = obj.try_as<wuxc::Border>()) b.BorderBrush(wuxm::SolidColorBrush(CurLime()));
        break;
    }
}

static void ReapplyAll()
{
    std::vector<StyledRef> snap;
    {
        std::lock_guard<std::mutex> l(g_styledMutex);
        snap = g_styledList;
    }
    for (auto const& item : snap)
    {
        try
        {
            // El weak_ref no se resuelve aquí: solo en el hilo de UI del elemento.
            if (!item.dispatcher) continue;
            auto weak = item.ref;
            auto role = item.role;
            auto id = item.id;
            item.dispatcher.RunAsync(winrt::Windows::UI::Core::CoreDispatcherPriority::Normal, [weak, role, id]()
            {
                try
                {
                    if (auto o = weak.get()) { ReapplyOne(role, o); return; }
                    std::lock_guard<std::mutex> l(g_styledMutex);   // el elemento ya no existe: se quita de la lista
                    g_styledList.erase(std::remove_if(g_styledList.begin(), g_styledList.end(),
                        [id](StyledRef const& r) { return r.id == id; }), g_styledList.end());
                }
                catch (...) {}
            });
        }
        catch (...) {}
    }
}

// Hilo que vigila el tema claro/oscuro y el acento de Windows y recarga la paleta sin reiniciar nada.
static DWORD WINAPI PaletteWatcher(LPVOID)
{
    const wchar_t* paths[2] = { L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", L"Software\\Microsoft\\Windows\\DWM" };
    HKEY keys[2]{};
    HANDLE events[2]{};
    DWORD n = 0;
    for (auto path : paths)
    {
        HKEY k = nullptr;
        if (RegOpenKeyExW(HKEY_CURRENT_USER, path, 0, KEY_NOTIFY, &k) != ERROR_SUCCESS) continue; // en procesos restringidos puede fallar: sin recarga en vivo
        HANDLE e = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (!e || RegNotifyChangeKeyValue(k, FALSE, REG_NOTIFY_CHANGE_LAST_SET, e, TRUE) != ERROR_SUCCESS)
        {
            if (e) CloseHandle(e);
            RegCloseKey(k);
            continue;
        }
        keys[n] = k; events[n] = e; n++;
    }
    if (!n) { Log(L"Paleta en vivo: sin claves que vigilar"); return 0; }
    for (;;)
    {
        DWORD first = WaitForMultipleObjects(n, events, FALSE, INFINITE);
        if (first >= WAIT_OBJECT_0 + n) return 0;
        first -= WAIT_OBJECT_0;
        Sleep(300); // agrupa varios cambios seguidos
        for (DWORD i = 0; i < n; i++)
        {
            // El evento que despertó el hilo ya se restableció solo; los demás se comprueban sin esperar.
            if (i == first || WaitForSingleObject(events[i], 0) == WAIT_OBJECT_0)
                RegNotifyChangeKeyValue(keys[i], FALSE, REG_NOTIFY_CHANGE_LAST_SET, events[i], TRUE);
        }
        try
        {
            LoadPalette();
            Color a = CurLime();
            Log(L"Paleta recargada: %s, acento #%02X%02X%02X", g_lightTheme ? L"claro" : L"oscuro", a.R, a.G, a.B);
            ReapplyAll();
        }
        catch (...) {}
    }
}

static void StartPaletteWatcher()
{
    static std::atomic<bool> started{ false };
    if (started.exchange(true)) return;
    if (HANDLE t = CreateThread(nullptr, 0, PaletteWatcher, nullptr, 0, nullptr)) CloseHandle(t);
}

// Aplica el color y lo mantiene: si Windows lo cambia (por ejemplo, al maximizar una ventana), se vuelve a poner.
static void KeepGlassFill(wuxs::Shape const& shape)
{
    shape.Fill(GlassBrush());
    shape.RegisterPropertyChangedCallback(wuxs::Shape::FillProperty(), [](wux::DependencyObject const& s, wux::DependencyProperty const&)
    {
        try { auto sh = s.as<wuxs::Shape>(); if (!IsCurrentGlass(sh.Fill())) sh.Fill(GlassBrush()); } catch (...) {}
    });
    Register(Role::GlassFill, shape);
    g_styled++;
}

static void KeepFill(wuxs::Shape const& shape, Role role)
{
    if (!SameColor(shape.Fill(), ColorFor(role))) shape.Fill(wuxm::SolidColorBrush(ColorFor(role)));
    shape.RegisterPropertyChangedCallback(wuxs::Shape::FillProperty(), [role](wux::DependencyObject const& s, wux::DependencyProperty const&)
    {
        try
        {
            auto sh = s.as<wuxs::Shape>();
            auto c = ColorFor(role);
            if (!SameColor(sh.Fill(), c)) sh.Fill(wuxm::SolidColorBrush(c));
        }
        catch (...) {}
    });
    Register(role, shape);
    g_styled++;
}

static void KeepBorderBackground(wuxc::Border const& border, Role role)
{
    if (!SameColor(border.Background(), ColorFor(role))) border.Background(wuxm::SolidColorBrush(ColorFor(role)));
    border.RegisterPropertyChangedCallback(wuxc::Border::BackgroundProperty(), [role](wux::DependencyObject const& s, wux::DependencyProperty const&)
    {
        try
        {
            auto b = s.as<wuxc::Border>();
            auto c = ColorFor(role);
            if (!SameColor(b.Background(), c)) b.Background(wuxm::SolidColorBrush(c));
        }
        catch (...) {}
    });
    Register(role, border);
    g_styled++;
}

static bool s_isExplorer = false;
static bool s_isStart = false;
static bool s_isSettings = false;

// Configuración: fondo grafito AUREVIX en el contenido de la ventana (en lugar de Mica). Solo el contenido de la
// ventana principal, nunca menús ni desplegables, que viven en otras raíces.
static winrt::weak_ref<wux::UIElement> s_settingsRoot;
static void StyleSettingsContent()
{
    try
    {
        auto window = wux::Window::Current();
        if (!window) return;
        auto content = window.Content();
        if (!content || s_settingsRoot.get() == content) return;
        if (SetPageBackground(content))
        {
            s_settingsRoot = winrt::make_weak(content);
            Register(Role::SettingsPage, content);
            g_styled++; Log(L"Configuración: fondo AUREVIX");
        }
    }
    catch (...) {}
}

// ------------------------------------------------------------------ animación de los iconos de la barra

// Animación al pasar el ratón: el icono crece un poco con un pequeño rebote y vuelve suave.
static void AnimateScale(wuxm::ScaleTransform const& st, double to, bool bounce)
{
    wuxa::Storyboard sb;
    for (auto prop : { L"ScaleX", L"ScaleY" })
    {
        wuxa::DoubleAnimation a;
        a.To(to);
        a.Duration(wux::DurationHelper::FromTimeSpan(std::chrono::milliseconds(bounce ? 260 : 200)));
        if (bounce) { wuxa::BackEase e; e.Amplitude(0.45); e.EasingMode(wuxa::EasingMode::EaseOut); a.EasingFunction(e); }
        else { wuxa::CubicEase e; e.EasingMode(wuxa::EasingMode::EaseOut); a.EasingFunction(e); }
        wuxa::Storyboard::SetTarget(a, st);
        wuxa::Storyboard::SetTargetProperty(a, prop);
        sb.Children().Append(a);
    }
    sb.Begin();
}

static void AddHoverZoom(wux::UIElement const& el)
{
    wuxm::ScaleTransform st;
    el.RenderTransformOrigin({ 0.5f, 0.85f });
    el.RenderTransform(st);
    el.PointerEntered([st](auto&&, auto&&) { try { AnimateScale(st, 1.2, true); } catch (...) {} });
    el.PointerExited([st](auto&&, auto&&) { try { AnimateScale(st, 1.0, false); } catch (...) {} });
    el.PointerCanceled([st](auto&&, auto&&) { try { AnimateScale(st, 1.0, false); } catch (...) {} });
    el.AddHandler(wux::UIElement::PointerPressedEvent(), winrt::box_value(wux::Input::PointerEventHandler(
        [st](auto&&, auto&&) { try { AnimateScale(st, 0.9, false); } catch (...) {} })), true);
    el.AddHandler(wux::UIElement::PointerReleasedEvent(), winrt::box_value(wux::Input::PointerEventHandler(
        [st](auto&&, auto&&) { try { AnimateScale(st, 1.2, true); } catch (...) {} })), true);
    g_styled++;
}

static HMODULE g_moduleForFiles() { return g_module; }

static bool HasNamedAncestor(wux::DependencyObject el, std::wstring_view name, int maxDepth = 6)
{
    for (int i = 0; i < maxDepth && el; i++)
    {
        el = wuxm::VisualTreeHelper::GetParent(el);
        if (auto fe = el.try_as<wux::FrameworkElement>(); fe && fe.Name() == name) return true;
    }
    return false;
}

// Logo AUREVIX (start-logo.png, junto a la DLL) en el botón de Inicio de la barra de tareas.
static void SetStartLogo(wuxc::Border const& host)
{
    wchar_t dll[MAX_PATH]{};
    GetModuleFileNameW(g_moduleForFiles(), dll, MAX_PATH);
    std::wstring path(dll);
    path = path.substr(0, path.find_last_of(L"\\/") + 1) + L"start-logo.png";
    winrt::com_ptr<IStream> stream;
    if (FAILED(SHCreateStreamOnFileEx(path.c_str(), STGM_READ | STGM_SHARE_DENY_WRITE, 0, FALSE, nullptr, stream.put()))) { Log(L"Logo: no se encontró %s", path.c_str()); return; }
    winrt::Windows::Storage::Streams::IRandomAccessStream ras{ nullptr };
    if (FAILED(CreateRandomAccessStreamOverStream(stream.get(), BSOS_DEFAULT, winrt::guid_of<winrt::Windows::Storage::Streams::IRandomAccessStream>(), winrt::put_abi(ras)))) return;
    winrt::Windows::UI::Xaml::Media::Imaging::BitmapImage bmp;
    bmp.SetSource(ras);
    wuxc::Image img;
    img.Source(bmp);
    img.Width(22); img.Height(22);
    img.HorizontalAlignment(wux::HorizontalAlignment::Center);
    img.VerticalAlignment(wux::VerticalAlignment::Center);
    img.IsHitTestVisible(false);
    host.Child(img);
    g_styled++;
    Log(L"Botón de Inicio: logo AUREVIX");
}

// Inicio AUREVIX: sin «Recomendado» (su fila queda a 0); la lista de ancladas conserva su tamaño.
static void CompactStart(wuxc::Grid const& root)
{
    try
    {
        auto rows = root.RowDefinitions();
        if (rows.Size() == 0) return;
        for (auto const& child : root.Children())
        {
            auto fe = child.try_as<wux::FrameworkElement>();
            if (!fe) continue;
            auto row = static_cast<uint32_t>(wuxc::Grid::GetRow(fe));
            if (row >= rows.Size()) continue;
            std::wstring_view n = fe.Name();
            bool suggestions = n == L"TopLevelSuggestionsListHeader" || n == L"ShowMoreSuggestions";
            if (!suggestions && n.empty())
            {
                int count = wuxm::VisualTreeHelper::GetChildrenCount(fe);
                for (int i = 0; i < count && !suggestions; i++)
                    if (auto c = wuxm::VisualTreeHelper::GetChild(fe, i).try_as<wux::FrameworkElement>())
                        suggestions = c.Name() == L"TopLevelSuggestionsContainer";
            }
            if (suggestions) { rows.GetAt(row).Height(wux::GridLengthHelper::FromPixels(0)); fe.Visibility(wux::Visibility::Collapsed); }

        }
    }
    catch (...) {}
}

static void StyleElement(wux::DependencyObject const& obj, std::wstring_view name)
{
    if (s_isExplorer)
    {
        if (auto shape = obj.try_as<wuxs::Shape>())
        {
            if (name == L"BackgroundFill" && HasAncestor(obj, L"Taskbar.TaskbarBackground"))
            {
                // Barra flotante AUREVIX: separada de los bordes y con esquinas redondeadas, sin borde.
                KeepGlassFill(shape);
                shape.Margin(wux::ThicknessHelper::FromLengths(6, 3, 6, 3));
                shape.StrokeThickness(0); // sin borde: solo el fondo AUREVIX
                if (auto rect = obj.try_as<wuxs::Rectangle>()) { rect.RadiusX(14); rect.RadiusY(14); }
            }
            else if (name == L"BackgroundStroke" && HasAncestor(obj, L"Taskbar.TaskbarBackground"))
                shape.Visibility(wux::Visibility::Collapsed); // la línea de Windows sobra con el borde redondeado
            else if (name == L"RunningIndicator") KeepFill(shape, Role::AppIndicator);
        }
        if (name == L"Icon" && HasNamedAncestor(obj, L"LaunchListButton"))
            if (auto ui = obj.try_as<wux::UIElement>()) { ui.Opacity(0); g_styled++; }
        if (name == L"BackgroundElement" && HasNamedAncestor(obj, L"LaunchListButton"))
            if (auto border = obj.try_as<wuxc::Border>()) { try { SetStartLogo(border); } catch (...) {} }
        return;
    }
    if (s_isSettings)
    {
        // Indicador de la sección seleccionada (barra lateral) en verde AUREVIX.
        if (name == L"SelectionIndicator" || name == L"ActiveIndicator")
            if (auto shape = obj.try_as<wuxs::Shape>()) KeepFill(shape, Role::SettingsSelection);
        return;
    }
    if (s_isStart)
    {
        if (name == L"RootPanel")
            if (auto grid = obj.try_as<wuxc::Grid>())
                if (auto parent = wuxm::VisualTreeHelper::GetParent(grid); parent && winrt::get_class_name(parent) == L"StartDocked.LauncherFrame")
                {
                    grid.VerticalAlignment(wux::VerticalAlignment::Bottom);
                    grid.Height(500); // buscador + 3 filas de apps ancladas + usuario
                    g_styled++;
                }
        if (name == L"TopLevelRoot")
            if (auto grid = obj.try_as<wuxc::Grid>())
            {
                grid.Loaded([grid](auto&&, auto&&) { CompactStart(grid); });
                grid.SizeChanged([grid](auto&&, auto&&) { CompactStart(grid); });
                CompactStart(grid);
            }
        // Menú Inicio AUREVIX: sin la sección «Recomendado»; solo tus apps.
        if (name == L"TopLevelSuggestionsContainer" || name == L"TopLevelSuggestionsListHeader" || name == L"ShowMoreSuggestions" ||
            name == L"NoTopLevelSuggestionsText" || name == L"NoSuggestionsShowSettings")
        {
            if (auto ui = obj.try_as<wux::UIElement>()) { ui.Visibility(wux::Visibility::Collapsed); g_styled++; }
            return;
        }
        if (auto border = obj.try_as<wuxc::Border>())
        {
            if (name == L"AcrylicBorder")
            {
                // Se conserva el acrílico de Windows (desenfoque real); el tinte AUREVIX va en AcrylicOverlay.
                border.CornerRadius(wux::CornerRadiusHelper::FromUniformRadius(16));
                border.BorderThickness(wux::ThicknessHelper::FromUniformLength(0)); // sin borde, más limpio
            }
            else if (name == L"AcrylicOverlay") { border.Background(GlassBrush()); Register(Role::StartOverlay, border); g_styled++; }
            else if (name == L"TopLevelSuggestionsBackground") border.Visibility(wux::Visibility::Collapsed);
            else if (name == L"StartDropShadow" || name == L"RootGridDropShadow" || name == L"DropShadow")
                border.CornerRadius(wux::CornerRadiusHelper::FromUniformRadius(16));
        }
    }
}

// ------------------------------------------------------------------ observador del árbol visual

struct VisualTreeWatcher : winrt::implements<VisualTreeWatcher, IVisualTreeServiceCallback2, winrt::non_agile>
{
    explicit VisualTreeWatcher(winrt::com_ptr<IUnknown> const& site) : m_diag(site.as<IXamlDiagnostics>())
    {
        // Llamar a AdviseVisualTreeChange desde el hilo de SetSite bloquea XAML: se hace desde otro hilo.
        AddRef();
        HANDLE thread = CreateThread(nullptr, 0, [](LPVOID p) -> DWORD
        {
            auto self = static_cast<VisualTreeWatcher*>(p);
            HRESULT hr = E_FAIL;
            try { hr = self->m_diag.as<IVisualTreeService3>()->AdviseVisualTreeChange(self); }
            catch (...) {}
            Log(L"AdviseVisualTreeChange: 0x%08X", static_cast<unsigned>(hr));
            self->Release();
            return 0;
        }, this, 0, nullptr);
        if (thread) CloseHandle(thread); else Release();
    }

    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation, VisualElement element, VisualMutationType mutation) noexcept override
    {
        if (mutation != Add) return S_OK;
        DumpElement(static_cast<unsigned long long>(relation.Parent), static_cast<unsigned long long>(element.Handle), element.Type, element.Name);
        if (s_isSettings) StyleSettingsContent();
        // Iconos de la barra (apps y botón de Inicio): animación al pasar el ratón.
        if (s_isExplorer && element.Type)
        {
            std::wstring_view type(element.Type);
            if (type == L"Taskbar.TaskListButton" || type == L"Taskbar.ExperienceToggleButton")
            {
                try
                {
                    winrt::Windows::Foundation::IInspectable o;
                    if (SUCCEEDED(m_diag->GetIInspectableFromHandle(element.Handle, reinterpret_cast<::IInspectable**>(winrt::put_abi(o)))) && o)
                        if (auto ui = o.try_as<wux::UIElement>()) AddHoverZoom(ui);
                }
                catch (...) {}
            }
        }
        if (!element.Name || !*element.Name) return S_OK;
        try
        {
            winrt::Windows::Foundation::IInspectable obj;
            if (FAILED(m_diag->GetIInspectableFromHandle(element.Handle, reinterpret_cast<::IInspectable**>(winrt::put_abi(obj)))) || !obj) return S_OK;
            if (auto dobj = obj.try_as<wux::DependencyObject>())
            {
                auto before = g_styled.load();
                StyleElement(dobj, element.Name);
                if (g_styled.load() != before && (g_styled.load() <= 3 || g_styled.load() % 20 == 0))
                    Log(L"Estilo aplicado a %s (%s). Total: %ld", element.Name, element.Type ? element.Type : L"?", g_styled.load());
            }
        }
        catch (...) {}
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) noexcept override { return S_OK; }

private:
    winrt::com_ptr<IXamlDiagnostics> m_diag;
};

// ------------------------------------------------------------------ objeto TAP que crea Windows

struct AurevixTap : winrt::implements<AurevixTap, IObjectWithSite, winrt::non_agile>
{
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) noexcept override
    {
        try
        {
            m_site.copy_from(site);
            if (!site) return S_OK;
            // Un solo observador por proceso aunque se conecte dos veces.
            if (g_active.exchange(true)) { Log(L"Ya activo en este proceso"); return S_OK; }
            auto name = ProcessName();
            s_isExplorer = name == L"explorer.exe";
            s_isStart = name == L"startmenuexperiencehost.exe";
            s_isSettings = name == L"systemsettings.exe";
            LoadPalette();
            StartPaletteWatcher();
            Log(L"AUREVIX Shell cargado en %s", name.c_str());
            OpenDump(name);
            m_watcher = winrt::make_self<VisualTreeWatcher>(m_site);
            if (s_isExplorer) if (HANDLE t = CreateThread(nullptr, 0, GlassExplorerWindows, nullptr, 0, nullptr)) CloseHandle(t);
            return S_OK;
        }
        catch (...) { return winrt::to_hresult(); }
    }

    HRESULT STDMETHODCALLTYPE GetSite(REFIID riid, void** ppv) noexcept override
    {
        if (!ppv) return E_POINTER;
        *ppv = nullptr;
        return m_site ? m_site->QueryInterface(riid, ppv) : E_FAIL;
    }

private:
    winrt::com_ptr<IUnknown> m_site;
    winrt::com_ptr<VisualTreeWatcher> m_watcher;
};

struct TapFactory : winrt::implements<TapFactory, IClassFactory>
{
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID riid, void** ppv) noexcept override
    {
        if (!ppv) return E_POINTER;
        *ppv = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        try { return winrt::make<AurevixTap>().as<IUnknown>()->QueryInterface(riid, ppv); }
        catch (...) { return winrt::to_hresult(); }
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) noexcept override { return S_OK; }
};

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    *ppv = nullptr;
    if (clsid != CLSID_AurevixTap) return CLASS_E_CLASSNOTAVAILABLE;
    try { return winrt::make<TapFactory>().as<IUnknown>()->QueryInterface(riid, ppv); }
    catch (...) { return winrt::to_hresult(); }
}

// La DLL se queda cargada mientras viva el proceso (los callbacks de XAML apuntan a su código).
STDAPI DllCanUnloadNow() { return S_FALSE; }

// ------------------------------------------------------------------ cristal en las ventanas del Explorador de archivos

static DWORD WINAPI GlassExplorerWindows(LPVOID)
{
    for (;;)
    {
        EnumWindows([](HWND hwnd, LPARAM) -> BOOL
        {
            DWORD pid = 0;
            GetWindowThreadProcessId(hwnd, &pid);
            if (pid != GetCurrentProcessId()) return TRUE;
            wchar_t cls[64]{};
            GetClassNameW(hwnd, cls, 64);
            if (wcscmp(cls, L"CabinetWClass") != 0) return TRUE;
            if (GetPropW(hwnd, L"AurevixGlass")) return TRUE;
            int backdrop = 3; // DWMSBT_TRANSIENTWINDOW: acrílico de Windows
            DwmSetWindowAttribute(hwnd, 38 /* DWMWA_SYSTEMBACKDROP_TYPE */, &backdrop, sizeof(backdrop));
            SetPropW(hwnd, L"AurevixGlass", reinterpret_cast<HANDLE>(1));
            return TRUE;
        }, 0);
        Sleep(1500);
    }
}

// ------------------------------------------------------------------ arranque automático dentro del proceso

static std::atomic<bool> g_started{ false };

// Windows solo acepta la conexión de diagnóstico de XAML hecha desde el propio proceso: se llama aquí dentro.
static DWORD WINAPI StartInProcess(LPVOID)
{
    using InitFn = HRESULT(WINAPI*)(LPCWSTR, DWORD, LPCWSTR, LPCWSTR, CLSID, LPCWSTR);
    HMODULE xaml = nullptr;
    for (int i = 0; i < 60 && !xaml; i++) { xaml = GetModuleHandleW(L"Windows.UI.Xaml.dll"); if (!xaml) Sleep(1000); }
    if (!xaml) xaml = LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    auto init = xaml ? reinterpret_cast<InitFn>(GetProcAddress(xaml, "InitializeXamlDiagnosticsEx")) : nullptr;
    if (!init) { Log(L"InitializeXamlDiagnosticsEx no disponible"); g_started = false; return 0; }
    wchar_t path[MAX_PATH]{};
    GetModuleFileNameW(g_module, path, MAX_PATH);
    HRESULT hr = E_FAIL;
    for (int attempt = 0; attempt < 30; attempt++)
    {
        for (int n = 1; n <= 20; n++)
        {
            wchar_t endpoint[64];
            swprintf_s(endpoint, L"VisualDiagConnection%d", n);
            hr = init(endpoint, GetCurrentProcessId(), nullptr, path, CLSID_AurevixTap, nullptr);
            if (hr != HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS)) break;
        }
        if (SUCCEEDED(hr) || g_active) break;
        Sleep(1000); // XAML todavía no ha creado su primera ventana
    }
    Log(L"Conexión en el proceso: 0x%08X", static_cast<unsigned>(hr));
    if (FAILED(hr) && !g_active) g_started = false;
    return 0;
}

// Procedimiento que el Control Center registra en el hilo de la ventana del proceso de destino.
extern "C" LRESULT CALLBACK AurevixHook(int code, WPARAM wParam, LPARAM lParam)
{
    if (!g_started.exchange(true))
    {
        auto name = ProcessName();
        if (name == L"explorer.exe" || name == L"startmenuexperiencehost.exe" || name == L"systemsettings.exe")
        {
            HMODULE pinned = nullptr; // que la DLL siga cargada aunque se retire el procedimiento
            GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                reinterpret_cast<LPCWSTR>(&AurevixHook), &pinned);
            if (HANDLE t = CreateThread(nullptr, 0, StartInProcess, nullptr, 0, nullptr)) CloseHandle(t);
            else g_started = false;
        }
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

// Estado para el Control Center: 1 si el estilo ya está activo en este proceso.
extern "C" int WINAPI AurevixIsActive() { return g_active ? 1 : 0; }

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) { g_module = module; DisableThreadLibraryCalls(module); }
    return TRUE;
}
