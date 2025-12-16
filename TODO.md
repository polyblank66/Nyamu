# TODO

## Shader Compilation Improvements

- [x] Доработать fuzzy search, чтобы пути с меньшим постфиксом имели больший приоритет. Например, запрос "Test" должен отдавать приоритет шейдеру "Test.shader" а не "Test (Something).shader".
- [x] При начале новой компиляции шейдеров сбрасывать `_lastSingleShaderResult`, `_lastAllShadersResult`, `_lastRegexShadersResult`
- [ ] Написать тесты для новых команд: `/compile-shaders-regex`, `/shader-compilation-status`
- [ ] Добавить новые команды в README.md
