-- Evaluates a transaction's conditions and applies the writes of the branch they choose. ARGV[4] is the number of
-- conditions, and three arguments follow for each: its kind, 'a' for absent, 'r' for revision equals or 'v' for value
-- equals; the record's member; and the revision or value it compares, '' for absent. The success branch follows, then
-- the failure branch: each is the number of its writes, and six arguments for each write: its kind, 'p' for put or
-- 'd' for delete; the record's member; its name; its value; its time to live in milliseconds, or 0 for a persistent
-- record or a session-bound one; and the session the record is bound to, or '' for a record bound to none, all three
-- '' for a delete.
-- Returns {0, revision} when the conditions held and {1, revision} when they didn't: the revision of the branch's
-- writes, or the keyspace's revision when the branch has none. Returns {2, revision} without writing, the keyspace's
-- revision, when a put of the chosen branch is bound to a session that is not live.
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
  position = position + 1 + 6 * tonumber(ARGV[position])
  status = 1
end
local count = tonumber(ARGV[position])
if count == 0 then
  return {status, string.format('%d', current())}
end
for i = 1, count do
  local at = position + 6 * (i - 1) + 1
  if ARGV[at] == 'p' and ARGV[at + 5] ~= '' and not live(ARGV[at + 5]) then
    return {2, string.format('%d', current())}
  end
end
local revision = advance()
for i = 1, count do
  local at = position + 6 * (i - 1) + 1
  if ARGV[at] == 'p' then
    put(ARGV[at + 1], ARGV[at + 2], ARGV[at + 3], tonumber(ARGV[at + 4]), revision, ARGV[at + 5])
  elseif find(ARGV[at + 1]) then
    remove(ARGV[at + 1], ARGV[at + 2])
  end
end
publish(revision)
return {status, revision}
