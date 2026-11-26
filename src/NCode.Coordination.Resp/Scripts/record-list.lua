-- Lists a page of the records under a prefix, in the lexicographic order of their members. ARGV[4] is where the page
-- starts: '[' and the prefix's member, or '(' and the previous page's continuation token. ARGV[5] is where it ends:
-- '(', the prefix's member and '~'. ARGV[6] is the most records on the page. The page examines at most its limit plus
-- 64 members, so members whose records have expired but are not yet deleted can leave it short, or empty, while more
-- members follow.
-- Returns {0, revision, last, name, revision, value, ...}: the keyspace's revision; the last member the page examined
-- when more members follow, or '' when none do; then the name, revision and value of each record.
local limit = tonumber(ARGV[6])
local budget = limit + 64
local reply = {0, string.format('%d', current()), ''}
local members = redis.call('ZRANGEBYLEX', index, ARGV[4], ARGV[5], 'LIMIT', 0, budget + 1)
local count = 0
local last = ''
local more = #members > budget
for i = 1, math.min(#members, budget) do
  local record = find(members[i])
  if record then
    if count == limit then
      more = true
      break
    end
    count = count + 1
    reply[#reply + 1] = record[2]
    reply[#reply + 1] = record[1]
    reply[#reply + 1] = record[3]
  end
  last = members[i]
end
if more then
  reply[3] = last
end
return reply
