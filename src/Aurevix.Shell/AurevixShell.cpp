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

namespace wux = winrt::Windows::UI::Xaml;
namespace wuxm = winrt::Windows::UI::Xaml::Media;
namespace wuxs = winrt::Windows::UI::Xaml::Shapes;
namespace wuxc = winrt::Windows::UI::Xaml::Controls;
namespace wuxa = winrt::Windows::UI::Xaml::Media::Animation;
using winrt::Windows::UI::Color;

// {8C2D5B1E-6A3F-4E8B-9D47-A7E15C0B3F21}
static constexpr GUID CLSID_AurevixTap = { 0x8c2d5b1e, 0x6a3f, 0x4e8b, { 0x9d, 0x47, 0xa7, 0xe1, 0x5c, 0x0b, 0x3f, 0x21 } };

// Paleta AUREVIX: clara (predeterminada) u oscura, según el modo de Windows.
static Color Graphite{ 0xF5, 0x0B, 0x0B, 0x0E };   // fondo de la barra y del menú (casi opaco: barato de dibujar)
static Color Lime{ 0xFF, 0x8D, 0xE0, 0x3C };       // acento
static Color Page{ 0xFF, 0x0B, 0x0B, 0x0E };       // fondo de Configuración

static void LoadPalette()
{
    DWORD light = 1, size = sizeof(light);
    RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", L"SystemUsesLightTheme",
                 RRF_RT_REG_DWORD, nullptr, &light, &size);
    if (light)
    {
        Graphite = { 0xF4, 0xF1, 0xF6, 0xEA };   // blanco con un toque verde AUREVIX
        Lime = { 0xFF, 0x4E, 0x9A, 0x1E };
        Page = { 0xFF, 0xF1, 0xF6, 0xEC };
    }
}

static std::atomic<bool> g_active{ false };
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

// Aplica el color y lo mantiene: si Windows lo cambia (por ejemplo, al maximizar una ventana), se vuelve a poner.
static void KeepFill(wuxs::Shape const& shape, Color color)
{
    if (!SameColor(shape.Fill(), color)) shape.Fill(wuxm::SolidColorBrush(color));
    shape.RegisterPropertyChangedCallback(wuxs::Shape::FillProperty(), [color](wux::DependencyObject const& s, wux::DependencyProperty const&)
    {
        try
        {
            auto sh = s.as<wuxs::Shape>();
            if (!SameColor(sh.Fill(), color)) sh.Fill(wuxm::SolidColorBrush(color));
        }
        catch (...) {}
    });
    g_styled++;
}

static void KeepBorderBackground(wuxc::Border const& border, Color color)
{
    if (!SameColor(border.Background(), color)) border.Background(wuxm::SolidColorBrush(color));
    border.RegisterPropertyChangedCallback(wuxc::Border::BackgroundProperty(), [color](wux::DependencyObject const& s, wux::DependencyProperty const&)
    {
        try
        {
            auto b = s.as<wuxc::Border>();
            if (!SameColor(b.Background(), color)) b.Background(wuxm::SolidColorBrush(color));
        }
        catch (...) {}
    });
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
        bool done = false;
        if (auto panel = content.try_as<wuxc::Panel>()) { panel.Background(wuxm::SolidColorBrush(Page)); done = true; }
        else if (auto control = content.try_as<wuxc::Control>()) { control.Background(wuxm::SolidColorBrush(Page)); done = true; }
        else if (auto border = content.try_as<wuxc::Border>()) { border.Background(wuxm::SolidColorBrush(Page)); done = true; }
        if (done) { s_settingsRoot = winrt::make_weak(content); g_styled++; Log(L"Configuración: fondo AUREVIX"); }
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
                KeepFill(shape, Graphite);
                shape.Margin(wux::ThicknessHelper::FromLengths(6, 3, 6, 3));
                shape.StrokeThickness(0); // sin borde: solo el fondo AUREVIX
                if (auto rect = obj.try_as<wuxs::Rectangle>()) { rect.RadiusX(14); rect.RadiusY(14); }
            }
            else if (name == L"BackgroundStroke" && HasAncestor(obj, L"Taskbar.TaskbarBackground"))
                shape.Visibility(wux::Visibility::Collapsed); // la línea de Windows sobra con el borde redondeado
            else if (name == L"RunningIndicator") KeepFill(shape, Lime);
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
            if (auto shape = obj.try_as<wuxs::Shape>()) KeepFill(shape, Lime);
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
                KeepBorderBackground(border, Graphite);
                border.CornerRadius(wux::CornerRadiusHelper::FromUniformRadius(16));
                border.BorderThickness(wux::ThicknessHelper::FromUniformLength(0)); // sin borde, más limpio
            }
            else if (name == L"AcrylicOverlay") border.Visibility(wux::Visibility::Collapsed);
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
            Log(L"AUREVIX Shell cargado en %s", name.c_str());
            OpenDump(name);
            m_watcher = winrt::make_self<VisualTreeWatcher>(m_site);
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
