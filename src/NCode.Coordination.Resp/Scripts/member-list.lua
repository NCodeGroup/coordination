-- Lists the members of a group whose session is live. ARGV[4] is the group. Returns {0, revision} followed by four
-- values for each member in order of node id: the node id, the session, the address and the encoded metadata.
local group = ARGV[4]
local reply = {0, string.format('%d', current())}
local entries = redis.call('ZRANGEBYLEX', members, '(' .. group .. ' ', '(' .. group .. '!')
for i = 1, #entries do
  local entry = entries[i]
  local fields = redis.call('HMGET', memberFields, 's ' .. entry, 'a ' .. entry, 'm ' .. entry)
  if fields[1] and fields[2] and fields[3] and live(fields[1]) then
    reply[#reply + 1] = string.sub(entry, #group + 2)
    reply[#reply + 1] = fields[1]
    reply[#reply + 1] = fields[2]
    reply[#reply + 1] = fields[3]
  end
end
return finish(reply)
