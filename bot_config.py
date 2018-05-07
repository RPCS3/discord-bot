"""
Variables:
{requestor} = Requestor Mention
{search_string} = Search String
{amount} = Amount Requested
{region} = Region
{media} = Blu-Ray/PSN
"""
search_header = "{requestor} searched for: ***{search_string}***"
newest_header = "{requestor} requested top {amount} newest {region} {media} updated games"
oldest_header = "{requestor} requested top {amount} oldest {region} {media} updated games"

invalid_command_text = "Invalid command!"

overflow_threshold = 1024 * 1024 * 256
latest_limit = 15

boot_up_message = "Hello and welcome to CompatBot. \n" \
                  "You can expect this message every time you crash this bot so please don't. \n" \
                  "All jokes aside if you manage to crash it please message Nicba1010 with the cause of the crash. \n" \
                  "Commands I would recommend using are:\n" \
                  "```" \
                  "!help\n" \
                  "!help top\n" \
                  "!top new 10 all playable\n" \
                  "!filters (It's a MUST)\n" \
                  "```" \
                  "I wish everyone here all the best of luck with using this bot and I will strive to improve it as " \
                  "often as possible.\n" \
                  "*Roberto Anic Banic AKA Nicba1010\n" \
                  "https://github.com/RPCS3/discord-bot"

bot_channel_id = 291679908067803136
bot_spam_id = 319224795785068545
bot_log_id = 436972161572536329
bot_rules_channel_id = 311894275015049216
bot_admin_id = 267367850706993152

user_moderation_character = '☕'
user_moderatable_channel_ids = [272875751773306881, 319224795785068545]
user_moderation_count_needed = 5
user_moderation_excused_roles = ['Administrator', 'Community Manager', 'Web Developer', 'Moderator',
                                 'Lead Graphics Developer', 'Lead Core Developer', 'Developers', 'Affiliated',
                                 'Contributors'
                                 ]
piracy_strings = [

]
faq_items = {}
