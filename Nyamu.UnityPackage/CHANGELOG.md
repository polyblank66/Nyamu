# [0.2.0](https://github.com/polyblank66/Nyamu/compare/v0.1.11...v0.2.0) (2026-08-25)


### Bug Fixes

* avoid CS1703 in code_execute reference collection #AI ([5cdd69f](https://github.com/polyblank66/Nyamu/commit/5cdd69f6e8ca719ad2abb39e2c3250bc77b319f7)), closes [#AI](https://github.com/polyblank66/Nyamu/issues/AI)
* defer code_execute fallback retry off buildFinished #AI ([ddaf6d9](https://github.com/polyblank66/Nyamu/commit/ddaf6d994cb6e755937c5844f6fc31c40060755f)), closes [#AI](https://github.com/polyblank66/Nyamu/issues/AI)
* exclude package tests from the exported unitypackage #AI ([bff943e](https://github.com/polyblank66/Nyamu/commit/bff943ea654503a134df578b602800b3aaa9f929)), closes [#AI](https://github.com/polyblank66/Nyamu/issues/AI)
* give code_execute class mode the default usings #AI ([2256df1](https://github.com/polyblank66/Nyamu/commit/2256df1d20dd7aa58bb355e4a014b8b4e6ec2de7)), closes [#AI](https://github.com/polyblank66/Nyamu/issues/AI)
* honest status for execute_menu_item timeout vs not-found ([9352460](https://github.com/polyblank66/Nyamu/commit/9352460fc699ef1e4792846bc615783b21c7accf))
* keep Unity idle-wait unconditional; truncate large JSON fields safely ([cdc173e](https://github.com/polyblank66/Nyamu/commit/cdc173e66545cfb876538317f7f67f50175da62d))
* never start the MCP server in asset import worker processes #AI ([04ce8d8](https://github.com/polyblank66/Nyamu/commit/04ce8d8e5ce579a499ea4df287bb62a65dfd9b6e)), closes [#AI](https://github.com/polyblank66/Nyamu/issues/AI)
* preserve typed Unity errors in remaining mcp-server.js call* handlers ([d1dc516](https://github.com/polyblank66/Nyamu/commit/d1dc51642d1192e5de231b3926bb3aa5d676fa4e))
* retry editor_status on domain-reload gap during compilation test #AI ([7d00045](https://github.com/polyblank66/Nyamu/commit/7d000455aae55bd6890dde2b4355930c330cda46)), closes [#AI](https://github.com/polyblank66/Nyamu/issues/AI)
* serve HTTP off the Unity main thread so an unfocused Play Mode cannot stall it #AI ([14b71fc](https://github.com/polyblank66/Nyamu/commit/14b71fc797dfa7db032aea7b723791dde01b5bdc)), closes [#AI](https://github.com/polyblank66/Nyamu/issues/AI)
* stop gating code_execute and Play Mode on a pending compilation #AI ([04113a2](https://github.com/polyblank66/Nyamu/commit/04113a2352ea386e54296ca5b4e865cce53bcd62)), closes [#AI](https://github.com/polyblank66/Nyamu/issues/AI)


### Features

* add code_execute MCP tool for running ad-hoc C# in the Editor ([c267c18](https://github.com/polyblank66/Nyamu/commit/c267c186ba1399d8561d2974bba34697e965bb06))
* add editor_enter_play_mode and expose editor_exit_play_mode over MCP ([d3d860b](https://github.com/polyblank66/Nyamu/commit/d3d860b492873800b2535d8cd47e8c35e5f406fe))
* keep serving MCP in Play Mode when the Editor is unfocused #AI ([392f7e1](https://github.com/polyblank66/Nyamu/commit/392f7e17801f97e3e8ded8a8046690db065534fc)), closes [#AI](https://github.com/polyblank66/Nyamu/issues/AI)

## [0.1.11](https://github.com/polyblank66/Nyamu/compare/v0.1.10...v0.1.11) (2026-03-07)


### Bug Fixes

* better compilaion errors detection for multiple script assemblies ([94f4499](https://github.com/polyblank66/Nyamu/commit/94f4499467a110c98f29924a5306e7b29a2a7122))

## [0.1.10](https://github.com/polyblank66/Nyamu/compare/v0.1.9...v0.1.10) (2026-03-06)


### Bug Fixes

* replace HttpListener with TcpListener to fix port conflict on domain reload ([16afa69](https://github.com/polyblank66/Nyamu/commit/16afa6982ec1c0da065532ce2883d005cb3b53db))

## [0.1.9](https://github.com/polyblank66/Nyamu/compare/v0.1.8...v0.1.9) (2026-03-05)


### Bug Fixes

* defer NewScene until after shader match to avoid HTTP server downtime [skip ci] ([f13e954](https://github.com/polyblank66/Nyamu/commit/f13e95464a9030a87182f41be27e7a40247765b8))

## [0.1.8](https://github.com/polyblank66/Nyamu/compare/v0.1.7...v0.1.8) (2026-02-14)


### Bug Fixes

* Clean up completed request handler tasks from active handlers ([7a36b76](https://github.com/polyblank66/Nyamu/commit/7a36b762f32282b5e58323e1f2e2a972c1ddf3d7))
* Modernize NyamuServer HTTP threading to async/await pattern ([8cf487b](https://github.com/polyblank66/Nyamu/commit/8cf487b7023c31d49373c8e71c605947e1c5a9aa))
* Wait for accept task completion to ensure port release ([a54d3ff](https://github.com/polyblank66/Nyamu/commit/a54d3ff6896337d1cf7c491b76230b79fe89c0be))

## [0.1.7](https://github.com/polyblank66/Nyamu/compare/v0.1.6...v0.1.7) (2026-02-12)


### Bug Fixes

* Increase HTTP listener retry window to 5 seconds to handle TIME_WAIT state ([11b975b](https://github.com/polyblank66/Nyamu/commit/11b975b92f08e59a7b3eb0cd690f408c64582928))

## [0.1.6](https://github.com/polyblank66/Nyamu/compare/v0.1.5...v0.1.6) (2026-02-10)


### Bug Fixes

* Catch all exceptions in HTTP listener retry loop to handle Mono's SocketException ([1ddf512](https://github.com/polyblank66/Nyamu/commit/1ddf512735cb387f501bf2483384678d196a184e))

## [0.1.5](https://github.com/polyblank66/Nyamu/compare/v0.1.4...v0.1.5) (2026-02-03)


### Bug Fixes

* Resolve issue with upm or git installation ([bc6a7a4](https://github.com/polyblank66/Nyamu/commit/bc6a7a42b6021eb0f4dcdecfa1d629f5ff805e4d))

## [0.1.4](https://github.com/polyblank66/Nyamu/compare/v0.1.3...v0.1.4) (2026-02-03)


### Bug Fixes

* add support for .unitypackage installation to NyamuBatGenerator ([af9c469](https://github.com/polyblank66/Nyamu/commit/af9c4691f6b333d9a42e65a7384ff4ac5c4d3963))

## [0.1.3](https://github.com/polyblank66/Nyamu/compare/v0.1.2...v0.1.3) (2026-02-03)


### Bug Fixes

* add .meta for copied README.md ([cbde8d6](https://github.com/polyblank66/Nyamu/commit/cbde8d671083fd5bf8a360934775e24272fd9fcf))

## [0.1.2](https://github.com/polyblank66/Nyamu/compare/v0.1.1...v0.1.2) (2026-02-01)


### Bug Fixes

* make package compile in Unity between 2021.3 and 6000.0, fix problem with com.unity.ext.nunit Build-in dependency ([68f0391](https://github.com/polyblank66/Nyamu/commit/68f039181e6532a86638b68178b1f789fc99a303))

## [0.1.1](https://github.com/polyblank66/Nyamu/compare/v0.1.0...v0.1.1) (2026-02-01)


### Bug Fixes

* initial release ([e8420fa](https://github.com/polyblank66/Nyamu/commit/e8420fa144d85b36bf749c98282081fe09df75a3))
