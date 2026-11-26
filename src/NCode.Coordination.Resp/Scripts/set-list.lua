-- Lists the live members of a set, those whose deadline is after the server's current time. KEYS[1] is the set.
-- Returns {0, member, ...}.
local time = redis.call('TIME')
local now = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)
local members = redis.call('ZRANGEBYSCORE', KEYS[1], '(' .. now, '+inf')
local reply = {0}
for index = 1, #members do
  reply[index + 1] = members[index]
end
return reply
