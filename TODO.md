# Nyamu Development TODOs

## Parallel Testing Improvements

### ✅ Completed
- [x] Автоматический поиск правильного Unity.exe для проекта через ProjectVersion.txt
  - Implemented automatic Unity.exe detection based on project Unity version
  - Search strategy: secondaryInstallPath.json, standard paths, multiple drives
  - Supports worker-specific project paths via environment variables

- [x] Решение конфликтов при записи в `NyamuProjectsRegistry.json`
  - Implemented pre-registration via Unity batch-mode with global file lock
  - Uses `filelock` for cross-process synchronization
  - Prevents race conditions when multiple workers start simultaneously
  - Sequential registration ensures registry integrity

### 🔄 In Progress
- [ ] Доработка параллельного тестирования
  - Basic parallel execution works
  - Registry conflicts resolved
  - Further optimization needed

### 📋 Planned
- [ ] Performance optimization for parallel test execution
- [ ] Improved error handling and logging for batch-mode operations
- [ ] Documentation for parallel testing setup and configuration
