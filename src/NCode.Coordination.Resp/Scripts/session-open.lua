-- Opens a session. ARGV[4] is the session's id and ARGV[5] its time to live in milliseconds.
-- Returns {0}.
redis.call('HSET', sessions, ARGV[4], ARGV[5])
redis.call('ZADD', sessionDeadlines, now + tonumber(ARGV[5]), ARGV[4])
return finish({0})
