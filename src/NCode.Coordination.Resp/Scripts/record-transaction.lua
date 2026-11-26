-- Evaluates a transaction's conditions and applies the writes of the branch they choose. ARGV[4] is the number of
-- conditions, and three arguments follow for each: its kind, 'a' for absent, 'r' for revision equals or 'v' for value
-- equals; the record's member; and the revision or value it compares, '' for absent. The success branch follows, then
-- the failure branch: each is the number of its writes, and five arguments for each write: its kind, 'p' for put or
-- 'd' for delete; the record's member; its name; its value; and its time to live in milliseconds, or 0 for a
-- persistent record, both '' for a delete.
-- Returns {0, revision} when the conditions held and {1, revision} when they didn't: the revision of the branch's
-- writes, or the keyspace's revision when the branch has none.
local position = 5
local held = true
for i = 1, tonumber(ARGV[4]) do
  if held then
    local kind = ARGV[position]
    local record = find(ARGV[position + 1])
    if kind == 'a' then
      held = record == nil
    elseif kind == 'r' then
      held = record ~= nil and record[1] == ARGV[position + 2]
    else
      held = record ~= nil and record[3] == ARGV[position + 2]
    end
  end
  position = position + 3
end
local status = 0
if not held then
  position = position + 1 + 5 * tonumber(ARGV[position])
  status = 1
end
local count = tonumber(ARGV[position])
if count == 0 then
  return {status, string.format('%d', current())}
end
local revision = advance()
for i = 1, count do
  local at = position + 5 * (i - 1) + 1
  if ARGV[at] == 'p' then
    put(ARGV[at + 1], ARGV[at + 2], ARGV[at + 3], tonumber(ARGV[at + 4]), revision)
  elseif find(ARGV[at + 1]) then
    remove(ARGV[at + 1], ARGV[at + 2])
  end
end
publish(revision)
return {status, revision}
