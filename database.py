from peewee import *
from playhouse.migrate import *

from bot_config import bot_admin_id

db = SqliteDatabase('bot.db')


class BaseModel(Model):
    class Meta:
        database = db


class Moderator(BaseModel):
    discord_id = IntegerField(unique=True)
    sudoer = BooleanField(default=False)


class PiracyString(BaseModel):
    string = CharField(unique=True)


class Warning(BaseModel):
    discord_id = IntegerField(index=True)
    issuer_id = IntegerField(default=0)
    reason = TextField()
    full_reason = TextField()


class Explanation(BaseModel):
    keyword = TextField(unique=True)
    text = TextField()

def init():
    with db:
        with db.atomic() as tx:
            db.get_tables()
            db.create_tables([Moderator, PiracyString, Warning, Explanation])
            try:
                Moderator.get(discord_id=bot_admin_id)
            except DoesNotExist:
                Moderator(discord_id=bot_admin_id, sudoer=True).save()
            tx.commit()

        migrator = SqliteMigrator(db)
        try:
            with db.atomic() as tx:
                migrate(
                    migrator.add_column('warning', 'issuer_id', IntegerField(default=0)),
                )
                tx.commit()
                print("Updated [warning] columns")
        except Exception as e:
            print(str(e))
            tx.rollback()
        try:
            with db.atomic() as tx:
                migrate(
                    migrator.add_index('warning', ('discord_id',), False),
                )
            tx.commit()
            print("Updated [warning] indices")
        except Exception as e:
            print(str(e))
            tx.rollback()
