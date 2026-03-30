from apps.telegram.app.bot.dispatch import TelegramCommandDispatcher
from apps.telegram.app.bot.handlers import TelegramMessageHandler
from apps.telegram.app.bot.models import CommandContext, IncomingMessage


def test_dispatcher_returns_start_message_for_start_command():
    dispatcher = TelegramCommandDispatcher()
    context = CommandContext(
        message=IncomingMessage(
            chat_id=1,
            user_id=2,
            chat_type="private",
            message_id=1,
            text="/start",
        ),
        command="/start",
        arguments="",
    )

    result = dispatcher.dispatch(context)

    assert result.chat_id == 1
    assert "quick capture is ready" in result.text.lower()


def test_message_handler_rejects_removed_capture_command_surface():
    handler = TelegramMessageHandler()

    result = handler.handle_update(
        {
            "message": {
                "chat": {"id": 10},
                "from": {"id": 11},
                "text": "/capture",
            }
        }
    )

    assert result is not None
    assert result.chat_id == 10
    assert result.text == "Only /start and /help are available. Send plain text to record quickly."
