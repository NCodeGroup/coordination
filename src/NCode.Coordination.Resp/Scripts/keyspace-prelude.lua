-- The start of every keyspace script, which the provider puts before the script's own text when it loads it.
-- KEYS[1] is the keyspace's revision. KEYS[2] holds its records: a hash with three fields per record, 'r:', 'n:' and
-- 'v:' followed by the record's member, which hold its revision, its name and its value, and a fourth, 's:' followed
-- by the member, when the record is bound to a session (the record is absent once that session is not live). KEYS[3]
-- is the index of the records' members, a sorted set whose scores are all 0, read in lexicographic order. KEYS[4]
-- holds the deadline, in
-- milliseconds on the server's clock, of each record that has a time to live.
-- KEYS[5] is the sessions: a hash from each session to its time to live in milliseconds. KEYS[6] holds the deadline
-- of each session, in milliseconds on the server's clock; 0 marks a session that has ended and is not yet cleaned up.
-- KEYS[7] holds the bindings of sessions: a sorted set whose scores are all 0 of '<session> <kind> <item>', where the
-- kind is 'g' for a grant field, 'r' for a record member and 'm' for a group member, '<group> <node>'.
-- KEYS[8] is the grants: a hash whose fields are 'l <name>' (a lease), 'e <name>' (a leadership), 'v <name>' (a
-- leadership's value), 's <slot> <name>' (a semaphore slot) and 'c <name>' (a semaphore's capacity), each holder line
-- being '<owner> <fencing> <session or ->'. KEYS[9] holds the deadline of each grant field that has a time to live.
-- KEYS[10] is the fencing counters, a hash with fields 'l <name>', 'e <name>' and 's <name>', which never expire.
-- KEYS[11] is the members of groups, a sorted set whose scores are all 0 of '<group> <node>'. KEYS[12] is the fields
-- of those members, a hash with fields 's <group> <node>' (the session), 'a <group> <node>' (the address),
-- 'm <group> <node>' (the metadata) and 'n <group> <session>' (the session's node in the group).
-- ARGV[1] is the keyspace's watch channel, ARGV[2] is '1' to publish on it with SPUBLISH and '0' with PUBLISH, and
-- ARGV[3] is how many milliseconds the records outlive their last deadline when none of them is persistent.
-- A record whose deadline is not after the server's current time is absent. Each change takes the keyspace's next
-- revision and publishes an event on the watch channel: the revision, then a line for each record it wrote, 'w' and
-- the record's name, or deleted, 'd' and the record's name, the lines separated by line feeds. The script first
-- deletes up to 64 records whose deadline has passed, as one change, and drops up to 64 grants whose deadline has
-- passed.
-- Functions the script's own text calls: live(session) is true when the session's deadline is after now. holdBackstop()
-- deletes the sessions, session deadlines, bindings, grants, grant deadlines, members and member fields keys when no
-- session deadline and no grant deadline is left, and else expires each of them ARGV[3] milliseconds after the latest
-- of the two deadlines (it never touches the fencing counters). publish(revision) sends the changes, then holds both
-- backstops, and finish(reply) holds the backstop of the ownership keys and returns the reply, for a script that
-- publishes nothing. holder(field) reads the grants hash and returns { owner, fencing, session } for a grant that is
-- held, or nil when the field is absent, its session is not live, or (for a grant with no session) its deadline in
-- KEYS[9] is missing or not after now. grant(field, owner, counter, session, timeToLive) takes the next fencing token
-- of the counter in KEYS[10], writes the holder line, then binds the field to the session in KEYS[7], or, with no
-- session, gives it a deadline in KEYS[9]; it returns the token. drop(field) removes a grant field, its binding or
-- deadline and, for a leadership 'e <name>', its value 'v <name>'. dropMember(group, node) removes a group member's
-- fields from KEYS[11] and KEYS[12], its binding and its session's node entry 'n <group> <session>'.
-- endSession(session, budget) marks the session ended with the deadline 0, then removes up to budget of its bindings
-- (a grant, a record, or a group member that still belongs to it); when it removed a record or a member it takes the
-- next revision and publishes it. It deletes the session once no binding is left, and returns the budget that remains.
-- The script then ends up to 16 sessions whose deadline has passed, within a budget of 64 bindings in all, before it
-- drops the lapsed grants. A grant, member or record bound to an ended session reads as absent through holder, live
-- and find even before it is removed.
local revisionKey = KEYS[1]
local records = KEYS[2]
local index = KEYS[3]
local deadlines = KEYS[4]
local sessions = KEYS[5]
local sessionDeadlines = KEYS[6]
local bindings = KEYS[7]
local grants = KEYS[8]
local grantDeadlines = KEYS[9]
local fencingKey = KEYS[10]
local members = KEYS[11]
local memberFields = KEYS[12]
local time = redis.call('TIME')
local now = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)
local changes = {}

local function current()
  return tonumber(redis.call('GET', revisionKey) or '1')
end

local function live(session)
  local deadline = redis.call('ZSCORE', sessionDeadlines, session)
  if not deadline then
    return false
  end
  return tonumber(deadline) > now
end

local function find(member)
  local deadline = redis.call('ZSCORE', deadlines, member)
  if deadline and tonumber(deadline) <= now then
    return nil
  end
  local fields = redis.call('HMGET', records, 'r:' .. member, 'n:' .. member, 'v:' .. member, 's:' .. member)
  if not fields[1] then
    return nil
  end
  if fields[4] and not live(fields[4]) then
    return nil
  end
  return fields
end

local function advance()
  local revision = string.format('%d', current() + 1)
  redis.call('SET', revisionKey, revision)
  return revision
end

local function put(member, name, value, timeToLive, revision, session)
  local previous = redis.call('HGET', records, 's:' .. member)
  if previous then
    redis.call('ZREM', bindings, previous .. ' r ' .. member)
  end
  redis.call('HSET', records, 'r:' .. member, revision, 'n:' .. member, name, 'v:' .. member, value)
  if session and session ~= '' then
    redis.call('HSET', records, 's:' .. member, session)
    redis.call('ZADD', bindings, 0, session .. ' r ' .. member)
  else
    redis.call('HDEL', records, 's:' .. member)
  end
  redis.call('ZADD', index, 0, member)
  if timeToLive > 0 then
    redis.call('ZADD', deadlines, now + timeToLive, member)
  else
    redis.call('ZREM', deadlines, member)
  end
  changes[#changes + 1] = 'w' .. name
end

local function remove(member, name)
  local previous = redis.call('HGET', records, 's:' .. member)
  if previous then
    redis.call('ZREM', bindings, previous .. ' r ' .. member)
  end
  redis.call('HDEL', records, 'r:' .. member, 'n:' .. member, 'v:' .. member, 's:' .. member)
  redis.call('ZREM', index, member)
  redis.call('ZREM', deadlines, member)
  changes[#changes + 1] = 'd' .. name
end

local function backstop()
  local count = redis.call('ZCARD', index)
  if count == 0 then
    redis.call('DEL', records, index, deadlines)
  elseif count > redis.call('ZCARD', deadlines) then
    redis.call('PERSIST', records)
    redis.call('PERSIST', index)
    redis.call('PERSIST', deadlines)
  else
    local last = redis.call('ZRANGE', deadlines, -1, -1, 'WITHSCORES')
    local timeToLive = string.format('%d', tonumber(last[2]) - now + tonumber(ARGV[3]))
    redis.call('PEXPIRE', records, timeToLive)
    redis.call('PEXPIRE', index, timeToLive)
    redis.call('PEXPIRE', deadlines, timeToLive)
  end
end

local function holdBackstop()
  if redis.call('ZCARD', sessionDeadlines) == 0 and redis.call('ZCARD', grantDeadlines) == 0 then
    redis.call('DEL', sessions, sessionDeadlines, bindings, grants, grantDeadlines, members, memberFields)
    return
  end
  local last = now
  local sessionLast = redis.call('ZRANGE', sessionDeadlines, -1, -1, 'WITHSCORES')
  if sessionLast[2] and tonumber(sessionLast[2]) > last then
    last = tonumber(sessionLast[2])
  end
  local grantLast = redis.call('ZRANGE', grantDeadlines, -1, -1, 'WITHSCORES')
  if grantLast[2] and tonumber(grantLast[2]) > last then
    last = tonumber(grantLast[2])
  end
  local timeToLive = string.format('%d', last - now + tonumber(ARGV[3]))
  redis.call('PEXPIRE', sessions, timeToLive)
  redis.call('PEXPIRE', sessionDeadlines, timeToLive)
  redis.call('PEXPIRE', bindings, timeToLive)
  redis.call('PEXPIRE', grants, timeToLive)
  redis.call('PEXPIRE', grantDeadlines, timeToLive)
  redis.call('PEXPIRE', members, timeToLive)
  redis.call('PEXPIRE', memberFields, timeToLive)
end

local function publish(revision)
  table.insert(changes, 1, revision)
  local event = table.concat(changes, '\n')
  if ARGV[2] == '1' then
    redis.call('SPUBLISH', ARGV[1], event)
  else
    redis.call('PUBLISH', ARGV[1], event)
  end
  changes = {}
  backstop()
  holdBackstop()
end

local function finish(reply)
  holdBackstop()
  return reply
end

local function holder(field)
  local line = redis.call('HGET', grants, field)
  if not line then
    return nil
  end
  local owner, fencing, session = string.match(line, '^(%S+) (%S+) (%S+)$')
  if session ~= '-' then
    if not live(session) then
      return nil
    end
  else
    local deadline = redis.call('ZSCORE', grantDeadlines, field)
    if not deadline or tonumber(deadline) <= now then
      return nil
    end
  end
  return { owner, fencing, session }
end

local function grant(field, owner, counter, session, timeToLive)
  local fencing = redis.call('HINCRBY', fencingKey, counter, 1)
  local line = owner .. ' ' .. string.format('%d', fencing) .. ' ' .. (session ~= '' and session or '-')
  redis.call('HSET', grants, field, line)
  if session ~= '' then
    redis.call('ZADD', bindings, 0, session .. ' g ' .. field)
  else
    redis.call('ZADD', grantDeadlines, now + timeToLive, field)
  end
  return fencing
end

local function drop(field)
  local line = redis.call('HGET', grants, field)
  if line then
    local session = select(3, string.match(line, '^(%S+) (%S+) (%S+)$'))
    if session and session ~= '-' then
      redis.call('ZREM', bindings, session .. ' g ' .. field)
    end
  end
  redis.call('HDEL', grants, field)
  redis.call('ZREM', grantDeadlines, field)
  if string.sub(field, 1, 2) == 'e ' then
    redis.call('HDEL', grants, 'v ' .. string.sub(field, 3))
  end
end

local function dropMember(group, node)
  local entry = group .. ' ' .. node
  local session = redis.call('HGET', memberFields, 's ' .. entry)
  redis.call('HDEL', memberFields, 's ' .. entry, 'a ' .. entry, 'm ' .. entry)
  redis.call('ZREM', members, entry)
  if session then
    redis.call('HDEL', memberFields, 'n ' .. group .. ' ' .. session)
    redis.call('ZREM', bindings, session .. ' m ' .. entry)
  end
end

local function endSession(session, budget)
  redis.call('ZADD', sessionDeadlines, 0, session)
  local bound = redis.call('ZRANGEBYLEX', bindings, '(' .. session .. ' ', '(' .. session .. '!', 'LIMIT', 0, budget)
  local visible = false
  for i = 1, #bound do
    local entry = bound[i]
    local kind = string.sub(entry, #session + 2, #session + 2)
    local item = string.sub(entry, #session + 4)
    if kind == 'g' then
      local line = redis.call('HGET', grants, item)
      if line and select(3, string.match(line, '^(%S+) (%S+) (%S+)$')) == session then
        drop(item)
      end
    elseif kind == 'r' then
      if redis.call('HGET', records, 's:' .. item) == session then
        remove(item, redis.call('HGET', records, 'n:' .. item))
        visible = true
      end
    elseif kind == 'm' then
      if redis.call('HGET', memberFields, 's ' .. item) == session then
        dropMember(string.match(item, '^(%S+) (.+)$'))
        visible = true
      end
    end
    redis.call('ZREM', bindings, entry)
  end
  if visible then
    publish(advance())
  end
  if #bound < budget then
    redis.call('HDEL', sessions, session)
    redis.call('ZREM', sessionDeadlines, session)
  end
  return budget - #bound
end

local expired = redis.call('ZRANGEBYSCORE', deadlines, '-inf', now, 'LIMIT', 0, 64)
if #expired > 0 then
  local revision = advance()
  for i = 1, #expired do
    remove(expired[i], redis.call('HGET', records, 'n:' .. expired[i]))
  end
  publish(revision)
end

local budget = 64
local endedSessions = redis.call('ZRANGEBYSCORE', sessionDeadlines, '-inf', now, 'LIMIT', 0, 16)
for i = 1, #endedSessions do
  if budget > 0 then
    budget = endSession(endedSessions[i], budget)
  end
end

local expiredGrants = redis.call('ZRANGEBYSCORE', grantDeadlines, '-inf', now, 'LIMIT', 0, 64)
for i = 1, #expiredGrants do
  drop(expiredGrants[i])
end
