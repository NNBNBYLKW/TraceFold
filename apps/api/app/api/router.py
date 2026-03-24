from __future__ import annotations

from app.api.system import router as system_router

from fastapi import APIRouter

from app.domains.ai_derivations.router import router as ai_derivations_router
from app.domains.alerts.router import router as alerts_router
from app.domains.capture.router import router as capture_router
from app.domains.dashboard.router import router as dashboard_router
from app.domains.expense.router import router as expense_router
from app.domains.health.router import router as health_router
from app.domains.knowledge.router import router as knowledge_router
from app.domains.pending.router import router as pending_router
from app.domains.system_tasks.router import router as system_tasks_router
from app.domains.workbench.router import router as workbench_router


api_router = APIRouter()

api_router.include_router(system_router)

api_router.include_router(
    alerts_router,
    prefix="/alerts",
    tags=["alerts"],
)

api_router.include_router(
    ai_derivations_router,
    prefix="/ai-derivations",
    tags=["ai-derivations"],
)

api_router.include_router(
    dashboard_router,
    prefix="/dashboard",
    tags=["dashboard"],
)

api_router.include_router(
    capture_router,
    prefix="/capture",
    tags=["capture"],
)

api_router.include_router(
    pending_router,
    prefix="/pending",
    tags=["pending"],
)

api_router.include_router(
    expense_router,
    prefix="/expense",
    tags=["expense"],
)

api_router.include_router(
    knowledge_router,
    prefix="/knowledge",
    tags=["knowledge"],
)

api_router.include_router(
    health_router,
    prefix="/health",
    tags=["health"],
)

api_router.include_router(
    workbench_router,
    prefix="/workbench",
    tags=["workbench"],
)

api_router.include_router(
    system_tasks_router,
    prefix="/tasks",
    tags=["tasks"],
)
