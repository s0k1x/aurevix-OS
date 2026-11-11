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
#include <atomic>
#include <cstdio>

#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>
#include <winrt/Windows.UI.Xaml.Controls.h>

namespace wux = winrt::Windows::UI::Xaml;
namespace wuxm = winrt::Windows::UI::Xaml::Media;
namespace wuxs = winrt::Windows::UI::Xaml::Shapes;
namespace wuxc = winrt::Windows::UI::Xaml::Controls;
using winrt::Windows::UI::Color;

// {8C2D5B1E-6A3F-4E8B-9D47-A7E15C0B3F21}
static constexpr GUID CLSID_AurevixTap = { 0x8c2d5b1e, 0x6a3f, 0x4e8b, { 0x9d, 0x47, 0xa7, 0xe1, 0x5c, 0x0b, 0x3f, 0x21 } };

// Paleta AUREVIX
static constexpr Color Graphite{ 0xF5, 0x0B, 0x0B, 0x0E };   // fondo de la barra y del menú (casi opaco: barato de dibujar)
static constexpr Color Line{ 0xFF, 0x26, 0x26, 0x2D };       // borde fino
static constexpr Color Lime{ 0xFF, 0x8D, 0xE0, 0x3C };       // acento

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
static void StyleWindowRoot(wux::DependencyObject const& obj)
{
    if (!s_isSettings) return;
    auto window = wux::Window::Current();
    if (!window) return;
    auto content = window.Content();
    if (!content || content != obj.try_as<wux::UIElement>()) return;
    if (auto panel = obj.try_as<wuxc::Panel>()) { panel.Background(wuxm::SolidColorBrush(Color{ 0xFF, 0x0B, 0x0B, 0x0E })); g_styled++; }
    else if (auto control = obj.try_as<wuxc::Control>()) { control.Background(wuxm::SolidColorBrush(Color{ 0xFF, 0x0B, 0x0B, 0x0E })); g_styled++; }
}

static void StyleElement(wux::DependencyObject const& obj, std::wstring_view name)
{
    if (s_isExplorer)
    {
        if (auto shape = obj.try_as<wuxs::Shape>())
        {
            if (name == L"BackgroundFill" && HasAncestor(obj, L"Taskbar.TaskbarBackground")) KeepFill(shape, Graphite);
            else if (name == L"BackgroundStroke" && HasAncestor(obj, L"Taskbar.TaskbarBackground")) KeepFill(shape, Line);
            else if (name == L"RunningIndicator") KeepFill(shape, Lime);
        }
        return;
    }
    if (s_isStart)
    {
        if (auto border = obj.try_as<wuxc::Border>())
        {
            if (name == L"AcrylicBorder")
            {
                KeepBorderBackground(border, Graphite);
                border.CornerRadius(wux::CornerRadiusHelper::FromUniformRadius(16));
                border.BorderBrush(wuxm::SolidColorBrush(Line));
                border.BorderThickness(wux::ThicknessHelper::FromUniformLength(1));
            }
            else if (name == L"AcrylicOverlay") border.Visibility(wux::Visibility::Collapsed);
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
        if (s_isSettings && relation.Parent == 0)
        {
            try
            {
                winrt::Windows::Foundation::IInspectable root;
                if (SUCCEEDED(m_diag->GetIInspectableFromHandle(element.Handle, reinterpret_cast<::IInspectable**>(winrt::put_abi(root)))) && root)
                    if (auto d = root.try_as<wux::DependencyObject>()) StyleWindowRoot(d);
            }
            catch (...) {}
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
            Log(L"AUREVIX Shell cargado en %s", name.c_str());
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

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(module);
    return TRUE;
}
