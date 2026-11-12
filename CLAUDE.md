# AUREVIX OS — instrucciones para Claude

- **Siempre**, al empezar cualquier petición: usa la skill `enrutador` (`.claude/skills/enrutador/SKILL.md`). Reparte el trabajo entre Opus (plan, decisiones, capturas), Sonnet (`aurevix-constructor`, `aurevix-revisor`) y Haiku (`aurevix-explorador`), y aplica sus reglas de ahorro de tokens.
- Lee `.claude/s0k1x-memoria.md` al empezar la sesión: ahí está lo aprendido (cómo compilar, probar en Windows real y publicar).
- Responde al usuario en español, corto y sin jerga. Nunca digas que algo funciona sin haberlo visto en CI o en capturas reales.
- Rama de trabajo: `claude/hola-q5pzma`. Publicar = PR a `main` + `release.yml` con la versión.
