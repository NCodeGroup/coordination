-- The startup probe: proves the server runs Lua and lets this user call TIME.
return redis.call('TIME')
