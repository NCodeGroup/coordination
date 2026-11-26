-- The startup probe: proves the server runs Lua and lets this user run scripts, and returns the server section of INFO.
return redis.call('INFO', 'server')
