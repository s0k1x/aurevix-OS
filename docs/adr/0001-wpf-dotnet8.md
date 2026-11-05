# ADR 0001 — Control Center con WPF y .NET 8

**Estado:** aceptada

## Contexto
El Control Center debe ser una app de escritorio mantenible, compatible con Windows 11 y compilable en CI. Opciones: WinUI 3, WPF, Electron/web.

## Decisión
**WPF sobre .NET 8**, autocontenido (`win-x64`).

## Razones
- Compila con el SDK de .NET normal (WinUI 3 exige el Windows App SDK y empaquetado MSIX/herramientas adicionales, con más fricción en CI).
- Madurez, rendimiento y soporte a largo plazo en Windows 11.
- Un diseño moderno se consigue igualmente con estilos propios (tema AUREVIX en `Themes/Aurevix.xaml`).
- Autocontenido: no obliga al usuario a instalar el runtime de .NET.

## Consecuencias
- No usa controles nativos Fluent (Mica, etc.); se puede incorporar más adelante.
- La interfaz se construye en código C# con un pequeño kit (`Views/Ui.cs`) para mantener coherencia y evitar XAML repetitivo.
- Se respeta el modo oscuro propio y el foco de teclado visible; falta una pasada completa de accesibilidad (lectores de pantalla) y de "reducir movimiento" (la opción existe en Ajustes pero hoy no hay animaciones que reducir).
