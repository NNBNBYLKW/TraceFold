from __future__ import annotations

from collections.abc import Callable

from fastapi import APIRouter, HTTPException
from sqlalchemy.orm import Session, sessionmaker

from media_api.taxonomy.schemas import TaxonomyNameWrite, TaxonomyRead
from media_api.api.errors import domain_api_error
from media_api.taxonomy.service import (
    AuthorService,
    EmptyTaxonomyName,
    SeriesService,
    TagService,
    TaxonomyNameConflict,
    TaxonomyNotFound,
    TaxonomyService,
)


ServiceFactory = Callable[[Session], TaxonomyService]


def build_taxonomy_router(
    session_factory: sessionmaker[Session],
) -> APIRouter:
    router = APIRouter(prefix="/api", tags=["taxonomy"])
    _register_resource(router, session_factory, "authors", AuthorService)
    _register_resource(router, session_factory, "series", SeriesService)
    _register_resource(router, session_factory, "tags", TagService)
    return router


def _register_resource(
    router: APIRouter,
    session_factory: sessionmaker[Session],
    resource: str,
    service_factory: ServiceFactory,
) -> None:
    @router.get(
        f"/{resource}",
        response_model=list[TaxonomyRead],
        name=f"list_{resource}",
    )
    def list_entries() -> list[TaxonomyRead]:
        with session_factory() as session:
            entries = service_factory(session).list()
            return [TaxonomyRead.from_model(entry) for entry in entries]

    @router.post(
        f"/{resource}",
        response_model=TaxonomyRead,
        name=f"create_{resource}",
    )
    def create_entry(payload: TaxonomyNameWrite) -> TaxonomyRead:
        with session_factory() as session:
            try:
                entry = service_factory(session).create(payload.name)
            except EmptyTaxonomyName as exc:
                raise domain_api_error(exc) from exc
            return TaxonomyRead.from_model(entry)

    @router.patch(
        f"/{resource}/{{entry_id}}",
        response_model=TaxonomyRead,
        name=f"update_{resource}",
    )
    def update_entry(
        entry_id: str,
        payload: TaxonomyNameWrite,
    ) -> TaxonomyRead:
        with session_factory() as session:
            try:
                entry = service_factory(session).update(entry_id, payload.name)
            except EmptyTaxonomyName as exc:
                raise domain_api_error(exc) from exc
            except TaxonomyNotFound as exc:
                raise domain_api_error(exc, details={"entry_id": entry_id}) from exc
            except TaxonomyNameConflict as exc:
                raise domain_api_error(exc) from exc
            return TaxonomyRead.from_model(entry)
