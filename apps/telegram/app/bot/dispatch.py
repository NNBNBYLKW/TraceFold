from .models import CommandContext, OutgoingMessage
from ..formatting import render_help, render_start, render_unknown_command


class TelegramCommandDispatcher:
    def dispatch(self, context: CommandContext) -> OutgoingMessage:
        if context.command == "/start":
            return render_start(context.message)

        if context.command == "/help":
            return render_help(context.message)

        return render_unknown_command(context.message)
