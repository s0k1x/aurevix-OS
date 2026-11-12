---
name: enrutador
description: Reparte cada tarea de AUREVIX entre Opus, Sonnet y Haiku y aplica las reglas de ahorro de tokens sin bajar la calidad. Úsala SIEMPRE al empezar cualquier petición del usuario en este proyecto (programar, arreglar, web, instalador, Shell, publicar, capturas), antes de leer archivos o lanzar trabajos.
---

# Enrutador AUREVIX

Objetivo: **misma potencia, menos tokens**. Lo difícil lo piensa el modelo más capaz; lo mecánico lo hace el más barato; y nada llega al hilo principal en bruto si puede llegar resumido.

## 1. Quién hace qué

| Trabajo | Quién | Modelo |
|---|---|---|
| Entender la petición, decidir el plan, hablar con el usuario, juzgar capturas, decisiones de diseño | Hilo principal | Opus (si la sesión no es Opus y la tarea es difícil, `aurevix-arquitecto`) |
| Diseño que cruza varios proyectos, fallo que no se entiende, elegir entre enfoques | `aurevix-arquitecto` (solo si el hilo principal no es Opus) | Opus |
| Implementar un cambio ya decidido en ≤5 archivos | `aurevix-constructor` | Sonnet |
| Revisar el diff antes de un push grande o de publicar | `aurevix-revisor` | Sonnet |
| Buscar dónde está algo, leer logs de CI largos, resumir un archivo grande | `aurevix-explorador` | Haiku |
| Cambios de 1-20 líneas que ya tienes delante, commits, push, lanzar workflows | Hilo principal, sin subagente | — |

Reglas de reparto:
- **Lanza en paralelo** lo independiente (p. ej. constructor de la web + constructor del instalador en el mismo mensaje), en segundo plano, y sigue trabajando mientras.
- Pasa a cada subagente **rutas exactas y el cambio concreto**, nunca «mira el proyecto». Un subagente que tiene que explorar gasta más que hacerlo tú.
- No delegues lo que cuesta menos hacer que explicar (ediciones pequeñas con el archivo ya leído).
- El hilo principal **siempre** revisa el resultado (diff o captura) antes de decir al usuario que algo está hecho.

## 2. Ahorro de tokens (se aplica siempre)

Lectura:
- `grep -n` / `sed -n 'A,Bp'` / `Read` con `offset`+`limit` antes que leer archivos enteros. Nunca releas un archivo que acabas de editar.
- Logs de CI: `get_job_logs` con `failed_only` y `tail_lines` 30-40; si hay que buscar en un log largo, `aurevix-explorador`.
- Capturas: a `deviceScaleFactor: 1`, recortadas a la zona que importa; mira solo las que cambian.

Espera:
- Nunca esperes en primer plano a CI ni a capturas: bucle en segundo plano (`run_in_background`) que termine cuando aparezca el commit en `ci-capturas`, y sigue con otra tarea. Si el usuario pregunta, mira el estado con una sola llamada.
- Agrupa cambios en un solo commit/push antes de lanzar `screenshots.yml` (cada ejecución tarda ~10 min).

Escritura:
- Ediciones con `Edit` o scripts de Python cortos con `assert` del texto a reemplazar; nada de reescribir archivos enteros salvo rediseños completos.
- Respuestas al usuario: corto, en español claro, sin jerga ni ids internos; primero el resultado, luego lo que falta.

Herramientas del kit que ayudan (si están instaladas): `s0k1x-tokens` (auditar consumo), `s0k1x-breve` (respuestas compactas), `s0k1x-equipo` (subagentes comprimidos), `s0k1x-memoria` (lo aprendido en `.claude/s0k1x-memoria.md`).

## 3. Flujo estándar de una petición

1. Leer `.claude/s0k1x-memoria.md` (solo al empezar la sesión).
2. Plan corto en el hilo principal (Opus). Si son varias tareas, `TaskCreate` una por tarea.
3. Localizar con `aurevix-explorador` lo que no sepas dónde está.
4. Implementar: lo pequeño en el hilo principal; lo mediano con `aurevix-constructor` (en paralelo si son áreas distintas).
5. `dotnet test tests/Aurevix.Tests -v q` en local; push; CI en segundo plano.
6. Antes de publicar: `aurevix-revisor` sobre `git diff main...HEAD`.
7. Probar en Windows real con `screenshots.yml`; mirar las capturas en el hilo principal (Opus) con ojo crítico.
8. Publicar (`release.yml`) solo con CI verde y capturas revisadas.
9. Apuntar en la memoria lo nuevo que haya costado descubrir.
