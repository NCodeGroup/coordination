-- Renews a live session for its time to live. ARGV[4] is the session's id.
-- Returns {0, time to live in milliseconds} when it renewed the session, and {1} when the session is not live.
if not live(ARGV[4]) then
  return finish({1})
end
local ttl = tonumber(redis.call('HGET', sessions, ARGV[4]))
redis.call('ZADD', sessionDeadlines, now + ttl, ARGV[4])
return finish({0, ttl})
