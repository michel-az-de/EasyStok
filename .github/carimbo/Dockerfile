# #3341: camada fina do build-images. Poe so o GIT_SHA do commit do master por cima da imagem que a PR
# ja buildou (BASE, sempre por digest). Sem COPY nem RUN: nenhuma camada nova, so o metadado muda;
# ENTRYPOINT, HEALTHCHECK, USER e EXPOSE vem da imagem de base. O workflow confere depois que o env
# da imagem tem exatamente um GIT_SHA igual ao sha, e o vps-deploy.sh confere de novo no container.
ARG BASE
FROM ${BASE}
ARG GIT_SHA
ENV GIT_SHA=${GIT_SHA}
