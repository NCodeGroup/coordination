-- The startup probe's permission check: ARGV holds every command the provider sends, and the script returns
-- those the server's user may not run, without running any of them. Returns {command, ...}, empty when the user
-- may run them all.
local denied = {}
for index = 1, #ARGV do
  if not redis.acl_check_cmd(ARGV[index]) then
    denied[#denied + 1] = ARGV[index]
  end
end
return denied
