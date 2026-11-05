# The only way out for the sandbox's dependency step: HTTPS to the package registries in squid.conf, nothing else.
FROM alpine:3.20
RUN apk add --no-cache squid
COPY squid.conf /etc/squid/squid.conf
EXPOSE 3128
CMD ["squid", "-N", "-d", "1"]
