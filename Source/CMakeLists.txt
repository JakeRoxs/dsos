#== == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == ==
#Rekindled Server
#Copyright(C) 2022 Tim Leonard
#Copyright(C) 2026 Jake Morgeson
#== == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == == ==

add_subdirectory(ThirdParty)

    if (WIN32)
        add_subdirectory(Loader)
            add_subdirectory(Injector)
                endif()
                    add_subdirectory(Loader.Avalonia)
                    add_subdirectory(Server)
                        add_subdirectory(Server.DarkSouls3)
                            add_subdirectory(Server.DarkSouls2)
                                add_subdirectory(Shared)
