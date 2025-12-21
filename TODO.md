# Nyamu MCP Server - TODO List

## Completed Tasks ✅

- [x] Переименовать `cancel-tests` в `tests-cancel`
- [x] Переименовать `test-status` в `tests-status`
- [x] Создать отдельную команду для запуска единственного теста `tests-run-single`
- [x] Создать отдельную команду для запуска всех тестов `tests-run-all`
- [x] Переименовать инструмент `run-tests` в `tests-run-regex`, убрать обычный фильтр, оставить только regex фильтр
- [x] Обновить README.md
- [x] Обновить AGENT-GUIDE.md
- [x] Обновить NyamuServer-API.postman_collection.json
- [x] Обновить пути в mcp_client.py для интеграционных тестов

## Next Steps

- [ ] Implement the new test endpoints in the Unity MCP server code
- [ ] Update the MCP tool definitions to match the new endpoint names
- [ ] Test all new endpoints with various scenarios
- [ ] Update integration tests to use the new endpoint names
- [ ] Verify backward compatibility considerations
- [ ] Document migration guide for users upgrading from older versions

## Backlog

- [ ] Add support for test result filtering and sorting
- [ ] Implement test history and trends analysis
- [ ] Add support for test categories and tags
- [ ] Implement parallel test execution
- [ ] Add support for test coverage reporting