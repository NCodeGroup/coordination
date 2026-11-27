-- Renews a grant that has a time to live. ARGV[4] is the grant's field, ARGV[5] its owner token and ARGV[6] the time to
-- live in milliseconds.
-- Returns {0} when it renewed the grant, and {1} when the grant is absent, another owner holds it or it is bound to a
-- session.
local h = holder(ARGV[4])
if not h or h[1] ~= ARGV[5] or h[3] ~= '-' then
  return finish({1})
end
redis.call('ZADD', grantDeadlines, now + tonumber(ARGV[6]), ARGV[4])
return finish({0})
