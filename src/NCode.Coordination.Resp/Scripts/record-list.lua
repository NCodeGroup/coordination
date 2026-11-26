-- Lists a page of the records under a prefix, in the lexicographic order of their members. ARGV[4] is where the page
-- starts: '[' and the prefix's member, or '(' and the member of the previous page's last record. ARGV[5] is where it
-- ends: '(', the prefix's member and '~'. ARGV[6] is the most records on the page.
-- Returns {0, revision, last, name, revision, value, ...}: the keyspace's revision; the member of the page's last
-- record when more records follow, or '' when none do; then the name, revision and value of each record.
local limit = tonumber(ARGV[6])
local reply = {0, string.format('%d', current()), ''}
local start = ARGV[4]
local count = 0
local last = ''
local more = false
while not more do
  local members = redis.call('ZRANGEBYLEX', index, start, ARGV[5], 'LIMIT', 0, limit + 1)
  for i = 1, #members do
    local record = find(members[i])
    if record then
      if count == limit then
        more = true
        break
      end
      count = count + 1
      last = members[i]
      reply[#reply + 1] = record[2]
      reply[#reply + 1] = record[1]
      reply[#reply + 1] = record[3]
    end
  end
  if #members <= limit then
    break
  end
  start = '(' .. members[#members]
end
if more then
  reply[3] = last
end
return reply
