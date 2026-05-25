FROM mcr.microsoft.com/devcontainers/dotnet:1-10.0

# Remove stale Yarn apt source/key from the base image so feature installs can
# run apt-get update without failing on a missing signing key.
RUN rm -f /etc/apt/sources.list.d/yarn.list \
    /etc/apt/keyrings/yarn-archive-keyring.gpg \
    /etc/apt/trusted.gpg.d/yarn.gpg

# Bake azd into the image to avoid repeated post-create installs.
RUN curl -fsSL https://aka.ms/install-azd.sh | bash \
    && ln -sf /root/.azd/bin/azd /usr/local/bin/azd
