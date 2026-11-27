-- Publishes a notification. KEYS[1] holds the topic's sequence, which never expires. ARGV[1] is the topic's channel,
-- ARGV[2] is '1' to publish on it with SPUBLISH and '0' with PUBLISH, and ARGV[3] is the payload.
-- Takes the topic's next sequence and publishes it, a line feed and the payload. Returns the sequence.
local sequence = redis.call('INCR', KEYS[1])
local message = string.format('%d', sequence) .. '\n' .. ARGV[3]
if ARGV[2] == '1' then
  redis.call('SPUBLISH', ARGV[1], message)
else
  redis.call('PUBLISH', ARGV[1], message)
end
return sequence
