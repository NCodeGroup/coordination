-- Joins a node to a group. ARGV[4] is the group, ARGV[5] the node id, ARGV[6] the session the member belongs to,
-- ARGV[7] the member's address and ARGV[8] its encoded metadata. A node that joins again under another node id leaves
-- its previous one, and a node id whose session is not live is taken over. Takes the next revision and publishes it
-- with a line 'm' and the group.
-- Returns {0} when the member joined, {1} when another live session holds the node id, and {2} when the session is not
-- live.
local group, node, session = ARGV[4], ARGV[5], ARGV[6]
if not live(session) then
  return finish({2})
end
local entry = group .. ' ' .. node
local current = redis.call('HGET', memberFields, 's ' .. entry)
if current and current ~= session and live(current) then
  return finish({1})
end
local revision = advance()
local previous = redis.call('HGET', memberFields, 'n ' .. group .. ' ' .. session)
if previous then
  dropMember(group, previous)
end
if current then
  dropMember(group, node)
end
redis.call('ZADD', members, 0, entry)
redis.call(
  'HSET',
  memberFields,
  's ' .. entry,
  session,
  'a ' .. entry,
  ARGV[7],
  'm ' .. entry,
  ARGV[8],
  'n ' .. group .. ' ' .. session,
  node
)
redis.call('ZADD', bindings, 0, session .. ' m ' .. entry)
changes[#changes + 1] = 'm' .. group
publish(revision)
return {0}
